"""Validate user TTFs and build isolated font-preview pak profiles for UI5.

The worker never installs a profile and never edits manifests, receipts, settings,
Core, or game files.  The launcher owns any later selection/publication step.
"""
from __future__ import annotations

import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata


WORKER_VERSION = 'font-worker-ui5-1'
PROFILE_DIR = Path('Fonts') / 'Profiles'
DEFAULT_PROFILE = Path('Fonts') / 'Default' / 'default_profile.json'
DEFAULT_MANIFEST = Path('Fonts') / 'Default' / 'default_manifest.json'
PAK_MANIFEST = Path('แพ็ก') / 'manifest.json'
PACK_FILENAME = 'zzz_NTE_Thai_Shops_999_P.pak'
ACCEPTED_PAK_SHA256 = '2c751d1a50eb2da2e630e489b59a43695425fb2db131a4c25a8aa73d0a804ff5'
ACCEPTED_NATIVE_SHA256 = '07bb681bca63b71108f54ba08198b402fdca36a57b3ed0a3b93994afad6596f8'
FONT_RELATIVE_PATHS = (
    'HT/Content/Environment/Building/TrainStation/Fonts/MiSans-Demibold.ufont',
    'HT/Content/Environment/Building/TrainStation/Fonts/MiSans-Heavy.ufont',
)
ORIGINAL_UFONT_HASHES = {
    'MiSans-Demibold.ufont': 'a98ec0e525f5a90f254023b15b5c0af432d38ae9ddc36f7606aba8c81cc2c2a4',
    'MiSans-Heavy.ufont': 'd6d2dfe1b32f25f587506042900f430be0f7366ad3fde515311dd81be9c160cc',
}
THAI_BLOCK = range(0x0E00, 0x0E80)
THAI_RUN = re.compile(r'[\u0e00-\u0e7f]+')
KNOWN_GAME_PROCESS_PREFIXES = ('htgame', 'ht-win64-shipping', 'nte', 'nevernesstoeverness')
KNOWN_LAUNCHER_PROCESS_NAMES = frozenset(('nteglobalgame', 'nteglobalbrowser', 'nteglobalwebbooster'))


def _is_game_process_name(name: str) -> bool:
    normalized = name.strip().lower()
    if normalized.endswith('.exe'):
        normalized = normalized[:-4]
    if normalized in KNOWN_LAUNCHER_PROCESS_NAMES:
        return False
    return normalized.startswith(KNOWN_GAME_PROCESS_PREFIXES)


class FontWorkerError(Exception):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code
        self.message = message


def _error(code: str, message: str):
    raise FontWorkerError(code, message)


def _emit(callback, stage: str, text: str):
    if callback:
        callback({'type': 'progress', 'stage': stage, 'text': text})


def _sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def _inside(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
        return True
    except ValueError:
        return False


def _has_reparse_attribute(path: Path) -> bool:
    try:
        st = path.lstat()
    except FileNotFoundError:
        return False
    attrs = getattr(st, 'st_file_attributes', 0)
    reparse = getattr(__import__('stat'), 'FILE_ATTRIBUTE_REPARSE_POINT', 0x400)
    return path.is_symlink() or bool(attrs & reparse)


def _assert_no_reparse_chain(app_root: Path, target: Path):
    """Reject junctions/symlinks in managed writes, including existing ancestors."""
    app = app_root.resolve(strict=True)
    raw = target.absolute()
    try:
        relative = raw.relative_to(app)
    except ValueError:
        _error('OUTPUT_OUTSIDE_APP', 'โฟลเดอร์ผลลัพธ์ต้องอยู่ภายในโฟลเดอร์แอป')
    chain = list(reversed(app.parents)) + [app]
    if any(_has_reparse_attribute(path) for path in chain):
        _error('REPARSE_PATH', 'ไม่อนุญาตให้เขียนผ่าน junction หรือ symlink ใน path ของแอป')
    current = app
    for part in relative.parts:
        current = current / part
        if _has_reparse_attribute(current):
            _error('REPARSE_PATH', 'ไม่อนุญาตให้เขียนผ่าน junction หรือ symlink ในโฟลเดอร์โปรไฟล์')
    resolved = target.resolve(strict=False)
    if not _inside(resolved, app):
        _error('OUTPUT_OUTSIDE_APP', 'เส้นทางผลลัพธ์ออกนอกโฟลเดอร์แอป')


def _load_libraries(app_root: Path):
    """Use packaged dependencies when present, and the app's explicit test path otherwise."""
    candidates = []
    env_path = os.environ.get('NTE_FONT_WORKER_PYDEPS')
    if env_path:
        candidates.append(Path(env_path))
        candidates.extend((app_root / 'Tools' / 'FontWorker' / 'pydeps',
                       app_root / 'Tools' / 'pydeps',
                       Path(__file__).resolve().parents[1] / 'pydeps',
                       Path(sys.executable).resolve().parent / 'pydeps'))
    for candidate in candidates:
        if candidate.is_dir() and str(candidate) not in sys.path:
            sys.path.insert(0, str(candidate))
    support = Path(__file__).resolve().parent / 'font_support'
    if str(support) not in sys.path:
        sys.path.insert(0, str(support))
    try:
        from fontTools.ttLib import TTFont
        from fontTools.ttLib.scaleUpem import scale_upem
        from fontTools.varLib.instancer import instantiateVariableFont
        import uharfbuzz
        import font_ui_patch
        import font_english_merge
    except Exception as exc:
        _error('DEPENDENCY_MISSING', f'โหลดเครื่องมือตรวจฟอนต์ไม่สำเร็จ: {exc}')
    return TTFont, scale_upem, instantiateVariableFont, uharfbuzz, font_ui_patch, font_english_merge


def _resolve_request(request: dict):
    if not isinstance(request, dict):
        _error('REQUEST_INVALID', 'คำขอต้องเป็น JSON object')
    required = {'action', 'app_root', 'font_path', 'source_pak', 'output_dir', 'native_path'}
    if not required <= set(request):
        _error('REQUEST_INVALID', 'คำขอขาดฟิลด์จำเป็น: ' + ', '.join(sorted(required - set(request))))
    action = request['action']
    if action not in ('validate', 'build'):
        _error('ACTION_INVALID', 'action ต้องเป็น validate หรือ build')
    role = request.get('role', 'thai')
    if action == 'validate' and role not in ('thai', 'english'):
        _error('ROLE_INVALID', 'role ต้องเป็น thai หรือ english')
    try:
        app_input = Path(request['app_root']).absolute()
        app_root = app_input.resolve(strict=True)
        font_path = Path(request['font_path']).resolve(strict=True)
        source_pak = Path(request['source_pak']).resolve(strict=True)
        native_path = Path(request['native_path']).resolve(strict=True)
        output_input = Path(request['output_dir']).absolute()
        output_dir = output_input.resolve(strict=False)
        role_paths_explicit = 'thai_font_path' in request or 'english_font_path' in request
        if role_paths_explicit and not {'thai_font_path', 'english_font_path'} <= set(request):
            _error('REQUEST_INVALID', 'คำขอ build ต้องระบุทั้ง thai_font_path และ english_font_path')
        if role_paths_explicit:
            thai_input = request['thai_font_path']
            english_input = request['english_font_path']
            thai_path = Path(thai_input).resolve(strict=True) if thai_input is not None else None
            english_path = Path(english_input).resolve(strict=True) if english_input is not None else None
        elif action == 'build':
            # Legacy build requests passed the Thai import as the single font_path.
            thai_path, english_path = font_path, None
        else:
            thai_path = english_path = None
    except (OSError, TypeError, ValueError) as exc:
        _error('PATH_INVALID', f'เส้นทางที่ระบุใช้ไม่ได้: {exc}')
    if not app_root.is_dir() or not font_path.is_file() or not source_pak.is_file() or not native_path.is_file():
        _error('PATH_INVALID', 'ไม่พบโฟลเดอร์แอป ฟอนต์ แพ็กต้นทาง หรือไฟล์ข้อความ')
    if font_path.suffix.lower() != '.ttf':
        _error('FONT_FORMAT', 'รองรับเฉพาะไฟล์ฟอนต์ .ttf')
    for role_path in (thai_path, english_path):
        if role_path is not None and (not role_path.is_file() or role_path.suffix.lower() != '.ttf'):
            _error('FONT_FORMAT', 'ฟอนต์บทบาทที่นำเข้าต้องเป็นไฟล์ .ttf')
    if app_input != app_root or output_input.parent != app_root / PROFILE_DIR:
        _error('OUTPUT_OUTSIDE_APP', 'เลือกโฟลเดอร์โปรไฟล์ย่อยโดยตรงใต้ Fonts/Profiles ของแอป')
    _assert_no_reparse_chain(app_root, output_input)
    return action, app_root, font_path, source_pak, output_dir, native_path, role, thai_path, english_path, role_paths_explicit


def _safe_app_member(app_root: Path, relative: str) -> Path:
    path = Path(relative)
    if path.is_absolute() or '..' in path.parts:
        _error('DEFAULT_PROFILE_INVALID', 'ค่าเส้นทางในโปรไฟล์เดิมไม่ปลอดภัย')
    resolved = (app_root / path).resolve(strict=True)
    if not _inside(resolved, app_root.resolve(strict=True)):
        _error('DEFAULT_PROFILE_INVALID', 'เส้นทางในโปรไฟล์เดิมออกนอกโฟลเดอร์แอป')
    return resolved


def _run_tool(args, *, binary=False):
    try:
        flags = getattr(subprocess, 'CREATE_NO_WINDOW', 0) if os.name == 'nt' else 0
        completed = subprocess.run([str(x) for x in args], stdin=subprocess.DEVNULL,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   creationflags=flags, check=False, timeout=180)
    except subprocess.TimeoutExpired:
        _error('TOOL_TIMEOUT', 'เครื่องมืออ่านหรือสร้างแพ็กใช้เวลานานเกินกำหนดและถูกหยุดแล้ว')
    except OSError as exc:
        _error('TOOL_MISSING', f'เรียกเครื่องมือแพ็กไม่สำเร็จ: {exc}')
    if completed.returncode != 0:
        detail = completed.stderr.decode('utf-8', 'replace').strip()
        _error('TOOL_FAILED', 'เครื่องมืออ่านหรือสร้างแพ็กไม่สำเร็จ' + (f': {detail[:300]}' if detail else ''))
    if binary:
        return completed.stdout
    return completed.stdout.decode('utf-8', 'replace')


def _tool_path(app_root: Path) -> Path:
    path = app_root / 'Tools' / 'repak.exe'
    if not path.is_file():
        _error('TOOL_MISSING', 'ไม่พบ Tools/repak.exe ในแอป')
    return path.resolve(strict=True)


def _read_bundle(app_root: Path, source_pak: Path, native_path: Path, repak: Path):
    profile_path = app_root / DEFAULT_PROFILE
    default_manifest_path = app_root / DEFAULT_MANIFEST
    pak_manifest_path = app_root / PAK_MANIFEST
    try:
        profile = json.loads(profile_path.read_text(encoding='utf-8-sig'))
        default_manifest = json.loads(default_manifest_path.read_text(encoding='utf-8-sig'))
        pak_manifest = json.loads(pak_manifest_path.read_text(encoding='utf-8-sig'))
    except Exception as exc:
        _error('DEFAULT_PROFILE_INVALID', f'อ่านโปรไฟล์หรือ manifest ค่าเดิมไม่สำเร็จ: {exc}')
    expected_pak = _safe_app_member(app_root, profile.get('default_pak', ''))
    expected_native = _safe_app_member(app_root, profile.get('native_candidate', ''))
    default_thai = _safe_app_member(app_root, profile.get('default_preview', ''))
    default_english = _safe_app_member(app_root, profile.get('english_default_preview', ''))
    if source_pak != expected_pak or native_path != expected_native:
        _error('SOURCE_PIN_MISMATCH', 'แพ็กและไฟล์ข้อความต้องเป็นต้นฉบับ v28c ที่แอปตรึงไว้')
    if (profile.get('default_pak_sha256') != ACCEPTED_PAK_SHA256
            or profile.get('candidate_sha256') != ACCEPTED_NATIVE_SHA256):
        _error('DEFAULT_PROFILE_MISMATCH', 'โปรไฟล์แอปไม่ตรงกับรุ่น v28c ที่อนุมัติ')
    if (not default_thai.is_file() or not default_english.is_file()
            or profile.get('english_default_preview_sha256') != '402d3ded24120a657916ab0c0253dfe1a2a21c8b4a6cb59be992c8a3288dcae4'
            or _sha256_file(default_english) != profile['english_default_preview_sha256']):
        _error('DEFAULT_FONT_MISMATCH', 'ไฟล์ฟอนต์ค่าเริ่มต้นอังกฤษไม่ตรงกับไฟล์ที่ตรึงไว้')
    if _sha256_file(source_pak) != ACCEPTED_PAK_SHA256:
        _error('SOURCE_PAK_MISMATCH', 'แพ็กต้นทางไม่ตรงกับค่า SHA-256 ของแพ็ก v28c ที่รับรองไว้')
    native_sha = _sha256_file(native_path)
    if native_sha != ACCEPTED_NATIVE_SHA256:
        _error('NATIVE_SOURCE_MISMATCH', 'ไฟล์ข้อความ 110,260 รายการไม่ตรงกับ candidate v28c ที่รับรองไว้')
    if (pak_manifest.get('CandidateSha256') != native_sha
            or pak_manifest.get('PackageRevision') != 'NTE-THAI-MAIN-v28c'):
        _error('PAK_MANIFEST_MISMATCH', 'manifest ของแพ็กไม่ตรงกับ candidate v28c')
    manifest_files = [x for x in default_manifest.get('Files', [])
                      if Path(x.get('Source', '')).as_posix() == Path(profile['default_pak']).as_posix()]
    if len(manifest_files) != 1 or manifest_files[0].get('Sha256') != profile['default_pak_sha256']:
        _error('DEFAULT_MANIFEST_MISMATCH', 'manifest ค่าเดิมไม่ยืนยัน SHA-256 ของแพ็กต้นทาง')
    expected_assets = profile.get('assets')
    if not isinstance(expected_assets, list) or len(expected_assets) != 7:
        _error('DEFAULT_PROFILE_INVALID', 'โปรไฟล์ค่าเดิมต้องระบุ asset ในแพ็กครบ 7 รายการ')
    asset_hashes = {x.get('path'): x.get('sha256') for x in expected_assets}
    if len(asset_hashes) != 7 or set(asset_hashes) != set(_asset_paths()):
        _error('DEFAULT_PROFILE_INVALID', 'รายชื่อ asset ในโปรไฟล์ไม่ตรงกับแพ็ก v28c')
    repak_output = _run_tool([repak, 'hash-list', source_pak])
    actual = _parse_hash_list(repak_output)
    if actual != asset_hashes:
        _error('SOURCE_ASSETS_MISMATCH', 'เนื้อหา 7 assets ในแพ็กไม่ตรงกับ manifest ที่ตรึงไว้')
    if 'V11' not in _run_tool([repak, 'info', source_pak]):
        _error('SOURCE_PAK_VERSION', 'แพ็กต้นทางไม่ใช่รูปแบบ V11 ที่กำหนด')
    clean_fonts = {}
    for name, digest in ORIGINAL_UFONT_HASHES.items():
        path = app_root / 'Fonts' / 'Original' / name
        if not path.is_file() or _sha256_file(path) != digest:
            _error('ORIGINAL_FONT_MISMATCH', f'ไฟล์ฟอนต์เดิม {name} ไม่ตรง SHA-256 ที่ตรึงไว้')
        clean_fonts[name] = path
    protected = {str(p): _sha256_file(p) for p in
                 (source_pak, native_path, profile_path, default_manifest_path, pak_manifest_path,
                  repak, default_thai, default_english, *clean_fonts.values())}
    return profile, default_manifest, pak_manifest, asset_hashes, clean_fonts, protected, native_sha, default_thai, default_english


def _asset_paths():
    return (
        'Engine/Plugins/Online/OnlineFramework/Content/Localization/OnlineFramework/en/OnlineFramework.locres',
        'Engine/Plugins/Online/OnlineSubsystem/Content/Localization/OnlineSubsystem/en/OnlineSubsystem.locres',
        'Engine/Plugins/Online/OnlineSubsystemUtils/Content/Localization/OnlineSubsystemUtils/en/OnlineSubsystemUtils.locres',
        FONT_RELATIVE_PATHS[0],
        FONT_RELATIVE_PATHS[1],
        'HT/Content/Localization/Game/en/game.locres',
        'HT/Plugins/OneEngineSDK/Content/Localization/OneEngineSDK/en/OneEngineSDK.locres',
    )


def _parse_hash_list(text: str):
    result = {}
    for line in text.splitlines():
        parts = line.strip().split(' ', 1)
        if len(parts) != 2:
            continue
        digest, name = parts
        result[name.replace('\\', '/')] = digest.lower()
    return result


def _native_corpus(path: Path):
    used, marks, runs, ascii_runs = set(), set(), set(), set()
    seen_keys = set()
    row_count = 0
    try:
        with path.open('r', encoding='utf-8-sig', newline='') as stream:
            for line_no, line in enumerate(stream, 1):
                if not line.strip():
                    continue
                try:
                    row = json.loads(line)
                except json.JSONDecodeError as exc:
                    _error('NATIVE_SOURCE_INVALID', f'JSONL ข้อความเสียหายที่บรรทัด {line_no}: {exc}')
                if not isinstance(row, dict) or not isinstance(row.get('text'), str):
                    _error('NATIVE_SOURCE_INVALID', f'แถวข้อความไม่ถูกต้องที่บรรทัด {line_no}')
                key = (row.get('namespace'), row.get('key'))
                if key in seen_keys:
                    _error('NATIVE_SOURCE_INVALID', f'key ซ้ำในไฟล์ข้อความที่บรรทัด {line_no}')
                seen_keys.add(key)
                row_count += 1
                text = row['text']
                for ch in text:
                    cp = ord(ch)
                    if cp in THAI_BLOCK:
                        used.add(cp)
                        if unicodedata.category(ch).startswith('M'):
                            marks.add(cp)
                runs.update(THAI_RUN.findall(text))
                ascii_runs.update(re.findall(r'[\x20-\x7e]+', text))
    except OSError as exc:
        _error('NATIVE_SOURCE_INVALID', f'อ่านไฟล์ข้อความไม่ได้: {exc}')
    if row_count != 110260:
        _error('NATIVE_SOURCE_COUNT', f'ต้องมีข้อความครบ 110,260 รายการ แต่พบ {row_count:,}')
    if not used:
        _error('NATIVE_SOURCE_NO_THAI', 'candidate ไม่มีอักขระไทยให้ตรวจ')
    return used, marks, sorted(runs), sorted(ascii_runs)


def _active_features(layout_table, script_tag):
    script_records = getattr(layout_table.ScriptList, 'ScriptRecord', [])
    record = next((x for x in script_records if x.ScriptTag == script_tag), None)
    if record is None:
        return set()
    systems = [record.Script.DefaultLangSys]
    systems.extend(x.LangSys for x in record.Script.LangSysRecord)
    indices = set()
    for system in systems:
        if system is None:
            continue
        indices.update(system.FeatureIndex)
        if system.ReqFeatureIndex != 0xFFFF:
            indices.add(system.ReqFeatureIndex)
    features = layout_table.FeatureList.FeatureRecord
    return {features[i].FeatureTag for i in indices if i < len(features)}


def _normalise_ttf(app_root: Path, font_path: Path, native_path: Path, emit, role='thai'):
    TTFont, scale_upem, instantiate_variable, _hb, _patch, _english_patch = _load_libraries(app_root)
    try:
        if font_path.stat().st_size > 32 * 1024 * 1024:
            _error('FONT_TOO_LARGE', 'ไฟล์ฟอนต์ต้องมีขนาดไม่เกิน 32 MB')
        raw = font_path.read_bytes()
        font = TTFont(io.BytesIO(raw), recalcBBoxes=False, recalcTimestamp=False, lazy=False)
        if 'glyf' not in font or 'CFF ' in font or 'CFF2' in font:
            _error('FONT_OUTLINE_UNSUPPORTED', 'ฟอนต์ต้องเป็น TrueType แบบ glyf; ไม่รองรับ CFF/outline แบบอื่น')
        variable_axes = []
        if 'fvar' in font:
            variable_axes = [{'tag': a.axisTag, 'default': a.defaultValue} for a in font['fvar'].axes]
            font = instantiate_variable(font, {}, inplace=False, static=True)
            if 'fvar' in font or 'gvar' in font:
                _error('FONT_VARIABLE_UNSUPPORTED', 'แปลง variable TTF ที่ค่าแกนเริ่มต้นเป็น static ไม่สำเร็จ')
        if 'OS/2' not in font:
            _error('FONT_INVALID', 'ฟอนต์ไม่มีตาราง OS/2 ที่จำเป็น')
        fs_type = int(font['OS/2'].fsType)
        restricted = fs_type & (0x0002 | 0x0004 | 0x0100 | 0x0200)
        if restricted:
            _error('FONT_EMBEDDING_RESTRICTED',
                   f'สิทธิ์ฝังฟอนต์ไม่อนุญาตการใช้งานนี้ (OS/2 fsType=0x{fs_type:04X})')
        cmap = font.getBestCmap() or {}
        used, marks, runs, ascii_runs = _native_corpus(native_path)
        if role == 'thai':
            missing = sorted(used - set(cmap))
            if missing:
                _error('THAI_GLYPH_MISSING', 'ฟอนต์ขาดอักขระไทยที่ใช้จริง: ' + ', '.join(f'U+{x:04X}' for x in missing[:12]))
            missing_marks = sorted(marks - set(cmap))
            if missing_marks:
                _error('THAI_MARK_MISSING', 'ฟอนต์ขาดสระหรือวรรณยุกต์ไทยที่ใช้จริง: '
                       + ', '.join(f'U+{x:04X}' for x in missing_marks[:12]))
            thai_gsub = _active_features(font['GSUB'].table, 'thai') if 'GSUB' in font else set()
            thai_gpos = _active_features(font['GPOS'].table, 'thai') if 'GPOS' in font else set()
            if not thai_gsub:
                _error('THAI_GSUB_MISSING', 'ฟอนต์ไม่มีชุดจัดรูปอักษร GSUB สำหรับภาษาไทย')
            if not {'mark', 'mkmk'} <= thai_gpos:
                _error('THAI_GPOS_MISSING', 'ฟอนต์ไม่มีชุดจัดตำแหน่งสระและวรรณยุกต์ไทย mark/mkmk')
            glyph_set = font.getGlyphSet()
            from fontTools.pens.recordingPen import RecordingPen
            empty_marks = []
            for cp in marks:
                pen = RecordingPen()
                glyph_set[cmap[cp]].draw(pen)
                if not pen.value:
                    empty_marks.append(cp)
            if empty_marks:
                _error('THAI_MARK_MISSING', 'glyph ของเครื่องหมายไทยไม่มีเส้นรูป: '
                       + ', '.join(f'U+{x:04X}' for x in sorted(empty_marks)[:12]))
        elif role == 'english':
            ascii_used = {ord(ch) for text in ascii_runs for ch in text}
            missing_ascii = sorted(ascii_used - set(cmap))
            if missing_ascii:
                _error('ENGLISH_ASCII_MISSING', 'ฟอนต์อังกฤษขาดอักขระ ASCII ที่ใช้จริง: '
                       + ', '.join(f'U+{x:04X}' for x in missing_ascii[:12]))
            glyph_set = font.getGlyphSet()
            from fontTools.pens.recordingPen import RecordingPen
            empty_ascii = []
            for cp in range(0x21, 0x7F):
                glyph_name = cmap.get(cp)
                if glyph_name is None:
                    continue
                pen = RecordingPen()
                glyph_set[glyph_name].draw(pen)
                if not pen.value:
                    empty_ascii.append(cp)
            if empty_ascii:
                _error('ENGLISH_ASCII_GLYPH_EMPTY', 'ฟอนต์อังกฤษมี glyph ASCII ที่มองเห็นได้แต่ไม่มีเส้นรูป: '
                       + ', '.join(f'U+{x:04X}' for x in empty_ascii[:12]))
            thai_gsub = _active_features(font['GSUB'].table, 'thai') if 'GSUB' in font else set()
            thai_gpos = _active_features(font['GPOS'].table, 'thai') if 'GPOS' in font else set()
        else:
            _error('ROLE_INVALID', 'role ต้องเป็น thai หรือ english')
        old_upem = int(font['head'].unitsPerEm)
        if old_upem <= 0:
            _error('FONT_INVALID', 'ค่า unitsPerEm ของฟอนต์ไม่ถูกต้อง')
        if old_upem != 1000:
            scale_upem(font, 1000)
        font.flavor = None
        font.recalcBBoxes = False
        font.recalcTimestamp = False
        output = io.BytesIO()
        font.save(output, reorderTables=True)
        normalized = output.getvalue()
        check = TTFont(io.BytesIO(normalized), recalcBBoxes=False, recalcTimestamp=False)
        if 'glyf' not in check or 'fvar' in check or check['head'].unitsPerEm != 1000:
            _error('FONT_NORMALIZE_FAILED', 'สร้าง preview TTF แบบ static และ UPEM 1000 ไม่สำเร็จ')
        details = {
            'role': role,
            'all_native_thai_codepoints_covered': role == 'thai',
            'thai_coverage_required': role == 'thai',
            'contains_thai': any(cp in cmap for cp in THAI_BLOCK),
            'native_thai_codepoints': len(used) if role == 'thai' else 0,
            'native_thai_mark_codepoints': len(marks) if role == 'thai' else 0,
            'required_ascii_covered': role == 'english',
            'required_ascii_codepoints': len({ord(ch) for text in ascii_runs for ch in text}),
            'latin_gsub_features': sorted(_active_features(font['GSUB'].table, 'latn')) if 'GSUB' in font else [],
            'latin_gpos_features': sorted(_active_features(font['GPOS'].table, 'latn')) if 'GPOS' in font else [],
            'missing_thai_codepoints': [], 'thai_gsub_features': sorted(thai_gsub),
            'thai_gpos_features': sorted(thai_gpos), 'used_thai_runs': len(runs),
            'used_ascii_runs': len(ascii_runs),
            'original_units_per_em': old_upem, 'normalized_units_per_em': 1000,
            'static_truetype_glyf': True, 'variable_axes_instanced_at_defaults': variable_axes,
            'fs_type': fs_type,
        }
        _emit(emit, 'validate', 'ตรวจ glyph ภาษาไทยและสร้าง preview TTF มาตรฐานแล้ว')
        return normalized, details, runs, ascii_runs, check
    except FontWorkerError:
        raise
    except Exception as exc:
        _error('FONT_INVALID', f'อ่านหรือปรับมาตรฐานไฟล์ TTF ไม่สำเร็จ: {exc}')


def _font_name(ttfont: object) -> str:
    try:
        names = ttfont['name'] if 'name' in ttfont else None
    except Exception:
        names = None
    if not names:
        return 'ชื่อฟอนต์ไม่ระบุ'
    for name_id in (4, 6, 1):
        for record in names.names:
            if record.nameID == name_id:
                try:
                    value = record.toUnicode().strip()
                except Exception:
                    continue
                if value:
                    return value
    return 'ชื่อฟอนต์ไม่ระบุ'


def _atomic_write(path: Path, data: bytes):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + '.tmp-fontworker')
    if temp.exists():
        _error('OUTPUT_TEMP_EXISTS', 'พบไฟล์ชั่วคราวเดิมในโฟลเดอร์โปรไฟล์')
    try:
        with temp.open('xb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp, path)
    except Exception:
        try:
            temp.unlink(missing_ok=True)
        except OSError:
            pass
        raise


def _snapshot_output(path: Path):
    return (path.read_bytes() if path.is_file() else None)


def _restore_output(path: Path, old_data):
    if old_data is None:
        path.unlink(missing_ok=True)
    else:
        _atomic_write(path, old_data)


def _assert_snapshot(snapshot):
    for raw_path, expected in snapshot.items():
        path = Path(raw_path)
        if not path.is_file() or _sha256_file(path) != expected:
            _error('SOURCE_CHANGED_DURING_RUN', f'ไฟล์ต้นทางเปลี่ยนระหว่างตรวจ: {path.name}')


def _assert_no_active_use(app_root: Path, output_dir: Path):
    active_receipts = [app_root / 'receipt.json', app_root / 'active_receipt.json',
                       app_root / 'Fonts' / 'active_receipt.json', output_dir / 'receipt.json']
    if any(p.exists() for p in active_receipts):
        _error('ACTIVE_RECEIPT', 'พบ receipt ของการติดตั้งที่ยังไม่คืนค่าเดิม จึงงดสร้างแพ็กฟอนต์')
    if os.name != 'nt':
        _error('PROCESS_CHECK_UNAVAILABLE', 'ตรวจสถานะโปรเซสเกมได้เฉพาะ Windows; งดสร้างแพ็ก')
    try:
        completed = subprocess.run(['tasklist', '/FO', 'CSV', '/NH'], stdin=subprocess.DEVNULL,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
                                   creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0), timeout=10)
    except subprocess.TimeoutExpired:
        _error('PROCESS_CHECK_UNAVAILABLE', 'ตรวจสถานะเกมใช้เวลานานเกินกำหนด จึงงดสร้างแพ็ก')
    except OSError as exc:
        _error('PROCESS_CHECK_UNAVAILABLE', f'ตรวจสถานะเกมไม่ได้ จึงงดสร้างแพ็ก: {exc}')
    if completed.returncode != 0:
        _error('PROCESS_CHECK_UNAVAILABLE', 'ตรวจสถานะเกมไม่ได้ จึงงดสร้างแพ็ก')
    import csv
    names = set()
    for row in csv.reader(completed.stdout.decode('utf-8', 'replace').splitlines()):
        if row:
            names.add(row[0].strip().lower())
    if any(_is_game_process_name(name) for name in names):
        _error('GAME_RUNNING', 'กรุณาปิดเกมก่อนสร้างแพ็กฟอนต์')


def _shape(hb, font_bytes: bytes, text: str):
    font = hb.Font(hb.Face(font_bytes))
    buffer = hb.Buffer()
    buffer.add_str(text)
    buffer.guess_segment_properties()
    buffer.script = 'Thai'
    buffer.language = 'th'
    hb.shape(font, buffer)
    return [(info.codepoint, info.cluster, pos.x_advance, pos.x_offset, pos.y_offset)
            for info, pos in zip(buffer.glyph_infos, buffer.glyph_positions)]


def _verify_corpus_shaping(hb, donor: bytes, revised_ufont: bytes, runs: list[str]):
    cooked = _unwrap_ufont(revised_ufont)
    checked = 0
    # Shape batches in one HarfBuzz call. A space keeps adjacent native runs
    # independent while avoiding hundreds of thousands of per-run face setups.
    batch_size = 128
    for start in range(0, len(runs), batch_size):
        batch = runs[start:start + batch_size]
        text = ' '.join(batch)
        expected = _shape(hb, donor, text)
        actual = _shape(hb, cooked, text)
        # The original ufont deliberately retains its own non-Thai space glyph;
        # exclude separators so this check covers donor Thai output only.
        expected = [row for row in expected if row[1] < len(text) and text[row[1]] != ' ']
        actual = [row for row in actual if row[1] < len(text) and text[row[1]] != ' ']
        if any(row[0] == 0 for row in actual):
            _error('PAK_THAI_GLYPH_MISSING', 'ฟอนต์ที่ผสานแล้วยังมีอักษรที่แสดงเป็น .notdef')
        if [x[1:] for x in expected] != [x[1:] for x in actual]:
            _error('PAK_THAI_SHAPING_MISMATCH',
                   f'ตำแหน่ง glyph ไทยหลังผสานไม่ตรงกับฟอนต์ที่เลือก: {batch[0][:40]}')
        checked += len(batch)
    return checked


def _unwrap_ufont(raw: bytes) -> bytes:
    import struct
    if len(raw) < 8:
        _error('UFONT_INVALID', 'ไฟล์ ufont สั้นกว่ารูปแบบที่รองรับ')
    size = struct.unpack_from('<I', raw)[0]
    if size != len(raw) - 8 or raw[-4:] != bytes(4):
        _error('UFONT_INVALID', 'wrapper ของไฟล์ ufont ไม่ถูกต้อง')
    return raw[4:-4]


def _files_equal(left: Path, right: Path):
    if left.stat().st_size != right.stat().st_size:
        return False
    with left.open('rb') as a, right.open('rb') as b:
        while True:
            aa, bb = a.read(1024 * 1024), b.read(1024 * 1024)
            if aa != bb:
                return False
            if not aa:
                return True


def _unpack(repak: Path, source: Path, output: Path):
    output.mkdir(parents=True, exist_ok=False)
    _run_tool([repak, 'unpack', '--quiet', '--strip-prefix', '../../../', '--output', output, source])
    files = {p.relative_to(output).as_posix() for p in output.rglob('*') if p.is_file()}
    if files != set(_asset_paths()):
        _error('PAK_READBACK_ASSETS', 'จำนวนหรือรายชื่อ asset ที่อ่านกลับจากแพ็กไม่ครบ 7 รายการ')


def _profile_metadata(font_path: Path, normalized: bytes, font_name: str, source_hash: str,
                      native_sha: str, repak: Path, clean_fonts: dict, details: dict, pak_hash=None):
    return {
        'worker_version': WORKER_VERSION, 'font_name': font_name,
        'input_font_name': font_path.name, 'input_font_sha256': source_hash,
        'normalized_preview_sha256': _sha256_bytes(normalized),
        'source_pak_sha256': None, 'candidate_sha256': native_sha,
        'repak_sha256': _sha256_file(repak), 'original_clean_ufonts': {
            name: _sha256_file(path) for name, path in clean_fonts.items()},
        'validation_details': details, 'generated_pak_sha256': pak_hash,
    }


def _build_pak(app_root, source_pak, output_dir, thai_normalized, thai_runs,
               english_normalized, ascii_runs, custom_thai, custom_english,
               assets, clean_fonts, repak, emit):
    TTFont, _scale, _inst, hb, patch, english_patch = _load_libraries(app_root)
    existed = output_dir.exists()
    output_dir.mkdir(parents=True, exist_ok=True)
    try:
        with tempfile.TemporaryDirectory(prefix='.font-worker-build-', dir=output_dir) as work_dir:
            work = Path(work_dir)
            original = work / 'source'
            payload = work / 'payload'
            readback = work / 'readback'
            _emit(emit, 'unpack', 'กำลังอ่าน asset ทั้ง 7 รายการจากแพ็กต้นฉบับ')
            _unpack(repak, source_pak, original)
            shutil.copytree(original, payload)
            merged_bytes = {}
            role_checks = {}
            for rel, filename in zip(FONT_RELATIVE_PATHS,
                                     ('MiSans-Demibold.ufont', 'MiSans-Heavy.ufont')):
                if custom_thai:
                    thai_base = clean_fonts[filename].read_bytes()
                    thai_ufont = patch.augment_ufont(thai_base, thai_normalized)
                    patch_details = patch.verify_ufont(thai_base, thai_ufont, thai_normalized)
                else:
                    # Accepted v28c fonts already contain the default Thai face.
                    thai_base = (original / Path(rel)).read_bytes()
                    thai_ufont = thai_base
                    patch_details = {'status': 'DEFAULT_THAI_PRESERVED'}
                thai_checked = _verify_corpus_shaping(hb, thai_normalized, thai_ufont, thai_runs)
                if custom_english:
                    merged = english_patch.replace_ascii_ufont(thai_ufont, english_normalized)
                    english_details = english_patch.verify_ascii_ufont(
                        thai_ufont, merged, english_normalized, thai_runs, ascii_runs)
                else:
                    merged = thai_ufont
                    english_details = {'status': 'DEFAULT_ENGLISH_PRESERVED'}
                merged_bytes[rel] = merged
                (payload / Path(rel)).write_bytes(merged)
                role_checks[filename] = {
                    'thai_source': 'custom' if custom_thai else 'accepted-baseline',
                    'english_source': 'custom' if custom_english else 'accepted-baseline',
                    'native_thai_runs_checked': thai_checked,
                    'thai': patch_details, 'english': english_details,
                }
                _emit(emit, 'merge', f'ผสานและตรวจฟอนต์ไทย/อังกฤษใน {filename} แล้ว')
            outpak_dir = work / 'out'
            outpak_dir.mkdir()
            temp_pak = outpak_dir / PACK_FILENAME
            _run_tool([repak, 'pack', '--quiet', '--version', 'V11', '--mount-point', '../../../',
                       payload, temp_pak])
            if not temp_pak.is_file():
                _error('PAK_BUILD_FAILED', 'repak ไม่สร้างไฟล์แพ็ก V11')
            _emit(emit, 'readback', 'กำลังอ่านกลับและเปรียบเทียบ asset ทั้ง 7 รายการ')
            _unpack(repak, temp_pak, readback)
            changed = []
            unchanged = []
            for rel in _asset_paths():
                expected_hash = assets[rel]
                read_path = readback / Path(rel)
                if rel in FONT_RELATIVE_PATHS:
                    target_hash = _sha256_bytes(merged_bytes[rel])
                    if _sha256_file(read_path) != target_hash or not _files_equal(read_path, payload / Path(rel)):
                        _error('PAK_FONT_READBACK_MISMATCH', f'อ่านกลับฟอนต์จากแพ็กไม่ตรง: {Path(rel).name}')
                    if target_hash != expected_hash:
                        changed.append(rel)
                else:
                    if _sha256_file(read_path) != expected_hash:
                        _error('PAK_NONFONT_CHANGED', f'asset ที่ไม่ใช่ฟอนต์เปลี่ยน: {rel}')
                    if not _files_equal(read_path, original / Path(rel)):
                        _error('PAK_NONFONT_CHANGED', f'asset ที่ไม่ใช่ฟอนต์ไม่เหมือนต้นฉบับ: {rel}')
                    unchanged.append(rel)
            if set(changed) - set(FONT_RELATIVE_PATHS) or len(unchanged) != 5:
                _error('PAK_ASSET_SCOPE', 'แพ็กที่สร้างเปลี่ยน asset นอกเหนือจากฟอนต์ 2 ไฟล์')
            list_text = _run_tool([repak, 'list', temp_pak])
            listed = {x.strip().replace('\\', '/') for x in list_text.splitlines() if x.strip()}
            if listed != set(_asset_paths()) or 'V11' not in _run_tool([repak, 'info', temp_pak]):
                _error('PAK_LAYOUT_INVALID', 'แพ็กที่สร้างไม่ใช่แพ็ก V11 ที่มี asset ครบ 7 รายการ')
            return temp_pak.read_bytes(), len(changed), len(unchanged), {
                'font_role_checks': role_checks,
            }
    except Exception:
        if not existed:
            try:
                if output_dir.exists() and not any(output_dir.iterdir()):
                    output_dir.rmdir()
            except OSError:
                pass
        raise


def _process(request: dict, emit=None):
    (action, app_root, font_path, source_pak, output_dir, native_path, role,
     thai_import_path, english_import_path, role_paths_explicit) = _resolve_request(request)
    if action == 'build':
        _assert_no_active_use(app_root, output_dir)
    repak = _tool_path(app_root)
    _emit(emit, 'source', 'กำลังตรวจ SHA-256 ของแพ็ก Candidate ฟอนต์เดิม และไฟล์ค่าเริ่มต้น')
    profile, default_manifest, pak_manifest, assets, clean_fonts, protected, native_sha, default_thai, default_english = \
        _read_bundle(app_root, source_pak, native_path, repak)

    if action == 'validate':
        input_sha = _sha256_file(font_path)
        expected_id = ('t_' if role == 'thai' else 'e_') + input_sha[:24]
        if output_dir.name != expected_id:
            _error('PROFILE_ID_MISMATCH', 'โฟลเดอร์ตรวจฟอนต์ต้องขึ้นต้นด้วย role และ SHA-256 24 หลักแรก')
        _emit(emit, 'font', 'กำลังตรวจฟอนต์ตามบทบาทที่เลือก')
        normalized, details, runs, ascii_runs, font = _normalise_ttf(app_root, font_path, native_path, emit, role)
        font_name = _font_name(font)
        preview_path = output_dir / 'preview.ttf'
        preview_before = _snapshot_output(preview_path)
        existed = output_dir.exists()
        _assert_snapshot(protected)
        _assert_no_reparse_chain(app_root, output_dir)
        _atomic_write(preview_path, normalized)
        try:
            _assert_snapshot(protected)
            if _sha256_file(font_path) != input_sha:
                _error('INPUT_FONT_CHANGED', 'ไฟล์ฟอนต์ต้นฉบับเปลี่ยนระหว่างตรวจ')
        except Exception:
            _restore_output(preview_path, preview_before)
            if not existed:
                try:
                    output_dir.rmdir()
                except OSError:
                    pass
            raise
        metadata = _profile_metadata(font_path, normalized, font_name, input_sha, native_sha,
                                     repak, clean_fonts, details)
        metadata.update({'role': role, 'source_pak_sha256': profile['default_pak_sha256']})
        _emit(emit, 'done', 'ตรวจฟอนต์ผ่านและบันทึก preview.ttf แล้ว')
        return {'type': 'result', 'success': True, 'action': action, 'role': role,
                'font_name': font_name, 'preview_path': str(preview_path), 'generated_pak': None,
                'sha256': {'font': input_sha, 'preview': _sha256_bytes(normalized),
                           'source_pak': profile['default_pak_sha256'], 'generated_pak': None},
                'profile_metadata': metadata, 'validation_details': details}

    custom_thai = thai_import_path is not None
    custom_english = english_import_path is not None
    if not custom_thai and not custom_english:
        _error('DEFAULT_BUILD_NOT_REQUIRED', 'ฟอนต์ค่าเริ่มต้นทั้งสองบทบาทไม่ต้องสร้างแพ็กใหม่')
    output_id = output_dir.name
    if role_paths_explicit and not re.fullmatch(r'(?:b_)?[0-9a-f]{32}', output_id):
        _error('PROFILE_ID_MISMATCH', 'โฟลเดอร์ build ต้องใช้ combined profile ID แบบ SHA-256 32 หลัก')

    thai_path = thai_import_path or default_thai
    english_path = english_import_path or default_english
    thai_normalized, thai_details, thai_runs, _ascii_runs_thai, thai_font = \
        _normalise_ttf(app_root, thai_path, native_path, emit, 'thai')
    english_normalized, english_details, _unused_runs, ascii_runs, english_font = \
        _normalise_ttf(app_root, english_path, native_path, emit, 'english')
    thai_name, english_name = _font_name(thai_font), _font_name(english_font)
    thai_sha, english_sha = _sha256_file(thai_path), _sha256_file(english_path)
    source_hashes = {str(thai_path): thai_sha, str(english_path): english_sha}
    preview_paths = {
        'thai': output_dir / 'thai-preview.ttf',
        'english': output_dir / 'english-preview.ttf',
    }
    pak_path = output_dir / 'preview.pak'
    old_outputs = {str(p): _snapshot_output(p) for p in (*preview_paths.values(), pak_path)}
    existed = output_dir.exists()
    _assert_no_reparse_chain(app_root, output_dir)
    try:
        _assert_no_active_use(app_root, output_dir)
        _emit(emit, 'build', 'กำลังสร้างแพ็กจากค่าเริ่มต้นที่ตรึงไว้และฟอนต์บทบาทที่เลือก')
        generated, changed_count, unchanged_count, merge_details = _build_pak(
            app_root, source_pak, output_dir, thai_normalized, thai_runs,
            english_normalized, ascii_runs, custom_thai, custom_english,
            assets, clean_fonts, repak, emit)
        _assert_snapshot(protected)
        for raw_path, digest in source_hashes.items():
            if _sha256_file(Path(raw_path)) != digest:
                _error('INPUT_FONT_CHANGED', 'ไฟล์ฟอนต์เปลี่ยนระหว่างสร้างแพ็ก')
        _assert_no_active_use(app_root, output_dir)
        _assert_no_reparse_chain(app_root, output_dir)
        for role_name, data in (('thai', thai_normalized), ('english', english_normalized)):
            path = preview_paths[role_name]
            if not (path.is_file() and _sha256_file(path) == _sha256_bytes(data)):
                _atomic_write(path, data)
        _atomic_write(pak_path, generated)
        _assert_snapshot(protected)
        for raw_path, digest in source_hashes.items():
            if _sha256_file(Path(raw_path)) != digest:
                _error('INPUT_FONT_CHANGED', 'ไฟล์ฟอนต์เปลี่ยนก่อนส่งผลลัพธ์')
        _assert_no_active_use(app_root, output_dir)

        thai_record = {
            'role': 'thai', 'font_name': thai_name, 'input_font_sha256': thai_sha,
            'normalized_preview_sha256': _sha256_bytes(thai_normalized),
            'preview_path': str(preview_paths['thai']), 'source': 'custom' if custom_thai else 'default',
        }
        english_record = {
            'role': 'english', 'font_name': english_name, 'input_font_sha256': english_sha,
            'normalized_preview_sha256': _sha256_bytes(english_normalized),
            'preview_path': str(preview_paths['english']), 'source': 'custom' if custom_english else 'default',
        }
        details = {
            'thai': thai_details, 'english': english_details,
            'pak_assets_changed': changed_count, 'pak_assets_unchanged': unchanged_count,
            **merge_details,
        }
        meta = {
            'worker_version': WORKER_VERSION, 'source_pak_sha256': profile['default_pak_sha256'],
            'candidate_sha256': native_sha, 'repak_sha256': _sha256_file(repak),
            'font_roles': {'thai': thai_record, 'english': english_record},
            'original_clean_ufonts': {name: _sha256_file(path) for name, path in clean_fonts.items()},
            'generated_pak_sha256': _sha256_bytes(generated), 'validation_details': details,
        }
        _emit(emit, 'done', 'สร้างแพ็ก preview V11 และอ่านกลับตรวจครบ 7 assets แล้ว')
        return {
            'type': 'result', 'success': True, 'action': action, 'role': 'thai',
            'font_name': thai_name, 'preview_path': str(preview_paths['thai']),
            'font_roles': {'thai': thai_record, 'english': english_record},
            'generated_pak': str(pak_path),
            'sha256': {'font': thai_sha if custom_thai else english_sha,
                       'preview': _sha256_bytes(thai_normalized),
                       'source_pak': profile['default_pak_sha256'],
                       'generated_pak': _sha256_bytes(generated),
                       'font_roles': {'thai': thai_sha, 'english': english_sha}},
            'profile_metadata': meta, 'validation_details': details,
        }
    except Exception:
        for path in (*preview_paths.values(), pak_path):
            _restore_output(path, old_outputs[str(path)])
        if not existed:
            try:
                if output_dir.exists() and not any(output_dir.iterdir()):
                    output_dir.rmdir()
            except OSError:
                pass
        raise


def run_request(request: dict, emit=None) -> dict:
    """Run one request and return one final result object; never writes outside output_dir."""
    action = request.get('action') if isinstance(request, dict) else None
    try:
        return _process(request, emit)
    except FontWorkerError as exc:
        return {'type': 'result', 'success': False, 'action': action,
                'error': {'code': exc.code, 'message': exc.message}}
    except Exception as exc:
        return {'type': 'result', 'success': False, 'action': action,
                'error': {'code': 'INTERNAL_ERROR', 'message': f'เกิดข้อผิดพลาดภายใน font worker: {exc}'}}


def main(argv=None):
    # Frozen workers are launched with redirected pipes by the launcher.
    # Force UTF-8 so Thai progress and JSON errors never depend on the console code page.
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, 'reconfigure', None)
        if reconfigure:
            reconfigure(encoding='utf-8')
    argv = list(sys.argv[1:] if argv is None else argv)
    if len(argv) != 2 or argv[0] != '--request':
        print(json.dumps({'type': 'result', 'success': False, 'action': None,
                          'error': {'code': 'CLI_USAGE', 'message': 'ใช้ --request ตามด้วย path ของ JSON'}},
                         ensure_ascii=False), flush=True)
        return 2
    try:
        request_path = Path(argv[1]).resolve(strict=True)
        request = json.loads(request_path.read_text(encoding='utf-8-sig'))
    except Exception as exc:
        print(json.dumps({'type': 'result', 'success': False, 'action': None,
                          'error': {'code': 'REQUEST_READ_FAILED', 'message': f'อ่านคำขอไม่สำเร็จ: {exc}'}},
                         ensure_ascii=False), flush=True)
        return 2
    messages = []
    final = run_request(request, messages.append)
    for item in messages:
        print(json.dumps(item, ensure_ascii=False), flush=True)
    print(json.dumps(final, ensure_ascii=False), flush=True)
    return 0 if final.get('success') else 1


if __name__ == '__main__':
    raise SystemExit(main())
