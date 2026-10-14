"""Replace only ASCII presentation in a cooked game font with a donor TTF.

The original glyph order, all non-ASCII mappings, outlines, and horizontal
metrics are kept. Donor glyphs and Latin layout lookups are appended under
fresh names, then only printable ASCII cmap entries are redirected.
"""
from __future__ import annotations

import copy
import io
import re
import struct
import xml.etree.ElementTree as ET

from fontTools import subset
from fontTools.pens.recordingPen import DecomposingRecordingPen, RecordingPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.ttLib import TTFont


ASCII = tuple(range(0x20, 0x7F))


def _unwrap(data: bytes) -> bytes:
    if len(data) < 8 or struct.unpack_from('<I', data)[0] != len(data) - 8 or data[-4:] != bytes(4):
        raise ValueError('Invalid cooked ufont wrapper')
    return data[4:-4]


def _wrap(data: bytes, trailer: bytes) -> bytes:
    return struct.pack('<I', len(data)) + data + trailer


def _rename(font: TTFont, mapping: dict[str, str]) -> TTFont:
    stream = io.StringIO()
    font.saveXML(stream)
    tree = ET.fromstring(stream.getvalue())
    for node in tree.iter():
        for key, value in list(node.attrib.items()):
            if value in mapping:
                node.set(key, mapping[value])
            elif ' ' in value or ',' in value:
                parts = re.split(r'([ ,])', value)
                if any(part in mapping for part in parts):
                    node.set(key, ''.join(mapping.get(part, part) for part in parts))
    renamed = TTFont()
    renamed.importXML(io.BytesIO(ET.tostring(tree, encoding='utf-8')))
    return renamed


def _shift_nested_lookups(value, offset: int):
    if isinstance(value, list):
        for item in value:
            _shift_nested_lookups(item, offset)
    elif hasattr(value, '__dict__'):
        for name, item in vars(value).items():
            if name == 'LookupListIndex' and isinstance(item, int):
                setattr(value, name, item + offset)
            else:
                _shift_nested_lookups(item, offset)


def _merge_gdef(target: TTFont, donor: TTFont) -> int:
    if 'GDEF' not in donor:
        return 0
    if 'GDEF' not in target:
        target['GDEF'] = copy.deepcopy(donor['GDEF'])
        return 0
    dst, src = target['GDEF'].table, donor['GDEF'].table
    for field in ('GlyphClassDef', 'MarkAttachClassDef'):
        source = getattr(src, field, None)
        if source is None:
            continue
        current = getattr(dst, field, None)
        if current is None:
            setattr(dst, field, copy.deepcopy(source))
        else:
            current.classDefs.update(source.classDefs)
    src_sets = getattr(src, 'MarkGlyphSetsDef', None)
    if src_sets is None:
        return 0
    dst_sets = getattr(dst, 'MarkGlyphSetsDef', None)
    offset = len(dst_sets.Coverage) if dst_sets else 0
    if dst_sets:
        dst_sets.Coverage.extend(copy.deepcopy(src_sets.Coverage))
        dst_sets.MarkSetCount = len(dst_sets.Coverage)
    else:
        dst.MarkGlyphSetsDef = copy.deepcopy(src_sets)
    dst.Version = max(dst.Version, 0x00010002)
    return offset


def _remap_feature_indices(layout):
    records = layout.FeatureList.FeatureRecord
    ordered = sorted(enumerate(records), key=lambda pair: pair[1].FeatureTag)
    mapping = {old: new for new, (old, _) in enumerate(ordered)}
    layout.FeatureList.FeatureRecord = [record for _, record in ordered]
    layout.FeatureList.FeatureCount = len(ordered)
    for script_record in layout.ScriptList.ScriptRecord:
        systems = [script_record.Script.DefaultLangSys]
        systems.extend(item.LangSys for item in script_record.Script.LangSysRecord)
        for system in systems:
            if system is None:
                continue
            system.FeatureIndex = [mapping[i] for i in system.FeatureIndex]
            if system.ReqFeatureIndex != 0xFFFF:
                system.ReqFeatureIndex = mapping[system.ReqFeatureIndex]


def _merge_latin_layout(target: TTFont, donor: TTFont, tag: str, mark_set_offset: int):
    if tag not in target and tag not in donor:
        return
    if tag not in target:
        target[tag] = copy.deepcopy(donor[tag])
        table = target[tag].table
        if not any(r.ScriptTag == 'latn' for r in table.ScriptList.ScriptRecord):
            fallback = next((r for r in table.ScriptList.ScriptRecord if r.ScriptTag == 'DFLT'), None)
            if fallback is not None:
                latin = copy.deepcopy(fallback)
                latin.ScriptTag = 'latn'
                table.ScriptList.ScriptRecord.append(latin)
                table.ScriptList.ScriptRecord.sort(key=lambda r: r.ScriptTag)
                table.ScriptList.ScriptCount = len(table.ScriptList.ScriptRecord)
        return
    dst = target[tag].table
    old_latin = next((r for r in dst.ScriptList.ScriptRecord if r.ScriptTag == 'latn'), None)
    if old_latin is not None:
        dst.ScriptList.ScriptRecord.remove(old_latin)
    if tag not in donor:
        dst.ScriptList.ScriptCount = len(dst.ScriptList.ScriptRecord)
        return
    src = donor[tag].table
    donor_script = (next((r for r in src.ScriptList.ScriptRecord if r.ScriptTag == 'latn'), None)
                    or next((r for r in src.ScriptList.ScriptRecord if r.ScriptTag == 'DFLT'), None))
    if donor_script is None:
        dst.ScriptList.ScriptCount = len(dst.ScriptList.ScriptRecord)
        return

    lookup_offset = len(dst.LookupList.Lookup)
    additions = copy.deepcopy(src.LookupList.Lookup)
    for lookup in additions:
        _shift_nested_lookups(lookup, lookup_offset)
        if lookup.LookupFlag & 0x0010:
            lookup.MarkFilteringSet += mark_set_offset
    dst.LookupList.Lookup.extend(additions)
    dst.LookupList.LookupCount = len(dst.LookupList.Lookup)
    feature_offset = len(dst.FeatureList.FeatureRecord)
    incoming = copy.deepcopy(src.FeatureList.FeatureRecord)
    for record in incoming:
        record.Feature.LookupListIndex = [i + lookup_offset for i in record.Feature.LookupListIndex]
    dst.FeatureList.FeatureRecord.extend(incoming)
    dst.FeatureList.FeatureCount = len(dst.FeatureList.FeatureRecord)

    target_script = copy.deepcopy(donor_script)
    target_script.ScriptTag = 'latn'
    systems = [target_script.Script.DefaultLangSys]
    systems.extend(item.LangSys for item in target_script.Script.LangSysRecord)
    for system in systems:
        if system is None:
            continue
        system.FeatureIndex = [i + feature_offset for i in system.FeatureIndex]
        if system.ReqFeatureIndex != 0xFFFF:
            system.ReqFeatureIndex += feature_offset
    dst.ScriptList.ScriptRecord.append(target_script)
    dst.ScriptList.ScriptRecord.sort(key=lambda r: r.ScriptTag)
    dst.ScriptList.ScriptCount = len(dst.ScriptList.ScriptRecord)
    _remap_feature_indices(dst)


def replace_ascii_ufont(original: bytes, donor_bytes: bytes) -> bytes:
    raw = _unwrap(original)
    target = TTFont(io.BytesIO(raw), recalcBBoxes=False, recalcTimestamp=False)
    donor = TTFont(io.BytesIO(donor_bytes), recalcBBoxes=False, recalcTimestamp=False)
    if 'glyf' not in donor or donor['head'].unitsPerEm != target['head'].unitsPerEm:
        raise ValueError('Donor must be normalized glyf TTF with matching unitsPerEm')
    if 'CFF ' not in target and 'glyf' not in target:
        raise ValueError('Unsupported target outline format')
    donor_map = donor.getBestCmap() or {}
    missing = [cp for cp in ASCII if cp not in donor_map]
    if missing:
        raise ValueError('English donor lacks required printable ASCII: ' + ', '.join(hex(x) for x in missing[:12]))

    options = subset.Options()
    options.hinting = False
    options.glyph_names = True
    options.layout_features = ['*']
    subsetter = subset.Subsetter(options=options)
    subsetter.populate(unicodes=ASCII)
    subsetter.subset(donor)
    old_order = list(target.getGlyphOrder())
    old_glyph_count = len(old_order)
    donor_order = [name for name in donor.getGlyphOrder() if name != '.notdef']
    if 'CFF ' in target:
        mapping = {name: f'cid{len(old_order) + i:05d}' for i, name in enumerate(donor_order)}
    else:
        mapping = {name: f'ui3_en_{i:05d}' for i, name in enumerate(donor_order)}
    renamed = _rename(donor, mapping)
    _convert_legacy_kern_to_gpos(renamed)
    renamed_map = renamed.getBestCmap() or {}
    target_map = target.getBestCmap() or {}
    if any(cp not in target_map for cp in ASCII):
        raise ValueError('Target font lacks a printable ASCII cmap entry to replace')

    donor_set = donor.getGlyphSet()
    # Force these lazy tables to decompile against the old glyph count first.
    hmtx = target['hmtx']
    hmetrics = hmtx.metrics
    vmtx = target['vmtx'] if 'vmtx' in target else None
    if vmtx is not None:
        _ = vmtx.metrics
    if 'CFF ' in target:
        top = target['CFF '].cff.topDictIndex[0]
        if top.ROS != ('Adobe', 'Identity', 0) or len(top.FDArray) != 1:
            raise ValueError('Unsupported target CFF font dictionary structure')
        private = top.FDArray[0].Private
        charstrings = top.CharStrings
        for source_name in donor_order:
            glyph_name = mapping[source_name]
            advance = donor['hmtx'].metrics[source_name][0]
            recording = DecomposingRecordingPen(donor_set)
            donor_set[source_name].draw(recording)
            width = None if advance == private.defaultWidthX else advance - private.nominalWidthX
            pen = T2CharStringPen(width, None)
            recording.replay(pen)
            charstring = pen.getCharString(private=private, globalSubrs=target['CFF '].cff.GlobalSubrs)
            charstrings.charStrings[glyph_name] = len(charstrings.charStringsIndex)
            charstrings.charStringsIndex.append(charstring)
            top.charset.append(glyph_name)
            top.FDSelect.append(0)
        top.CIDCount = len(old_order) + len(donor_order)
    else:
        glyph_set = target.getGlyphSet()
        for source_name in donor_order:
            recording = DecomposingRecordingPen(donor_set)
            donor_set[source_name].draw(recording)
            pen = TTGlyphPen(None)
            recording.replay(pen)
            added_glyph = pen.glyph()
            added_glyph.recalcBounds(target['glyf'])
            target['glyf'].glyphs[mapping[source_name]] = added_glyph
    target.setGlyphOrder(old_order + [mapping[name] for name in donor_order])
    target['maxp'].numGlyphs = len(old_order) + len(donor_order)
    target['hhea'].numberOfHMetrics = target['maxp'].numGlyphs
    for source_name in donor_order:
        hmetrics[mapping[source_name]] = donor['hmtx'].metrics[source_name]
        if vmtx is not None:
            vmtx.metrics[mapping[source_name]] = vmtx.metrics['.notdef']
    if 'vhea' in target and vmtx is not None:
        target['vhea'].numberOfVMetrics = target['maxp'].numGlyphs

    for table in target['cmap'].tables:
        if table.isUnicode() and table.format in (4, 12, 13):
            for cp in ASCII:
                source_name = renamed_map.get(cp)
                if not source_name:
                    raise ValueError(f'English donor cmap missing {hex(cp)} after subset')
                table.cmap[cp] = source_name

    mark_set_offset = _merge_gdef(target, renamed)
    for tag in ('GSUB', 'GPOS'):
        _merge_latin_layout(target, renamed, tag, mark_set_offset)
    if 'DSIG' in target:
        del target['DSIG']
    stream = io.BytesIO()
    target.save(stream)
    return _wrap(stream.getvalue(), original[-4:])


def _convert_legacy_kern_to_gpos(font: TTFont) -> None:
    """Expose legacy kern pairs as a Latin GPOS feature for modern shapers."""
    if 'kern' not in font or 'GPOS' in font:
        return
    pairs = []
    for subtable in font['kern'].kernTables:
        if getattr(subtable, 'format', None) != 0:
            continue
        pairs.extend((left, right, value) for (left, right), value in subtable.kernTable.items()
                     if value and left != '.notdef' and right != '.notdef')
    if not pairs:
        return
    from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
    lines = ['languagesystem DFLT dflt;', 'languagesystem latn dflt;', 'feature kern {']
    # HarfBuzz represents legacy horizontal kerning as an offset plus advance
    # on the right glyph. Keep that geometry when expressing it as GPOS.
    lines.extend(f'  pos {left} <0 0 {value // 2} 0> {right} '
                 f'<{value // 2} 0 {value - value // 2} 0>;'
                 for left, right, value in sorted(pairs))
    lines.append('} kern;')
    addOpenTypeFeaturesFromString(font, '\n'.join(lines), tables=['GPOS'])


def _shape(hb, raw: bytes, text: str, script: str, language: str):
    font = hb.Font(hb.Face(raw))
    buffer = hb.Buffer()
    buffer.add_str(text)
    buffer.guess_segment_properties()
    buffer.script = script
    buffer.language = language
    hb.shape(font, buffer)
    return [(i.codepoint, i.cluster, p.x_advance, p.x_offset, p.y_offset)
            for i, p in zip(buffer.glyph_infos, buffer.glyph_positions)]


def verify_ascii_ufont(original: bytes, revised: bytes, donor_bytes: bytes,
                       thai_runs: list[str], ascii_runs: list[str]) -> dict:
    import uharfbuzz as hb

    before_raw, after_raw = _unwrap(original), _unwrap(revised)
    before = TTFont(io.BytesIO(before_raw), recalcBBoxes=False, recalcTimestamp=False)
    after = TTFont(io.BytesIO(after_raw), recalcBBoxes=False, recalcTimestamp=False)
    donor = TTFont(io.BytesIO(donor_bytes), recalcBBoxes=False, recalcTimestamp=False)
    before_map, after_map = before.getBestCmap(), after.getBestCmap()
    preserved_cps = [cp for cp in before_map if cp not in ASCII]
    for cp in preserved_cps:
        old_name, new_name = before_map[cp], after_map.get(cp)
        if new_name is None or before.getGlyphID(old_name) != after.getGlyphID(new_name):
            raise ValueError(f'Non-ASCII cmap/glyph ID changed for U+{cp:04X}')
        if before['hmtx'].metrics[old_name] != after['hmtx'].metrics[new_name]:
            raise ValueError(f'Non-ASCII metrics changed for U+{cp:04X}')
    preserved_names = {before_map[cp] for cp in preserved_cps}
    before_set, after_set = before.getGlyphSet(), after.getGlyphSet()
    for name in preserved_names:
        left, right = RecordingPen(), RecordingPen()
        before_set[name].draw(left)
        after_set[name].draw(right)
        if left.value != right.value:
            raise ValueError(f'Non-ASCII outline changed for original glyph {name}')
    for attr in ('ascent', 'descent', 'lineGap'):
        if getattr(before['hhea'], attr) != getattr(after['hhea'], attr):
            raise ValueError('Original line metrics changed')
    if 'CFF ' in after:
        charstrings = after['CFF '].cff.topDictIndex[0].CharStrings
        for name in after.getGlyphOrder()[len(before.getGlyphOrder()):]:
            charstring = charstrings[name]
            charstring.draw(RecordingPen())
            if charstring.width != after['hmtx'].metrics[name][0]:
                raise ValueError(f'Appended CFF width disagrees with hmtx for {name}')

    glyph_set = after.getGlyphSet()
    for cp in range(0x21, 0x7F):
        name = after_map.get(cp)
        if name is None:
            raise ValueError(f'Visible ASCII U+{cp:04X} is unmapped')
        pen = RecordingPen()
        glyph_set[name].draw(pen)
        if not pen.value:
            raise ValueError(f'Visible ASCII U+{cp:04X} has an empty outline')

    donor_raw = donor_bytes
    checked_ascii = 0
    for start in range(0, len(ascii_runs), 128):
        batch = ascii_runs[start:start + 128]
        text = ' '.join(batch)
        expected = _shape(hb, donor_raw, text, 'Latn', 'en')
        actual = _shape(hb, after_raw, text, 'Latn', 'en')
        if any(row[0] == 0 for row in actual):
            raise ValueError('English output contains .notdef glyphs')
        if [row[1:] for row in expected] != [row[1:] for row in actual]:
            raise ValueError('English Latin shaping differs from imported donor')
        checked_ascii += len(batch)

    checked_thai = 0
    for start in range(0, len(thai_runs), 128):
        batch = thai_runs[start:start + 128]
        text = ' '.join(batch)
        before_shape = _shape(hb, before_raw, text, 'Thai', 'th')
        after_shape = _shape(hb, after_raw, text, 'Thai', 'th')
        before_shape = [row for row in before_shape if row[1] < len(text) and text[row[1]] != ' ']
        after_shape = [row for row in after_shape if row[1] < len(text) and text[row[1]] != ' ']
        if [row[1:] for row in before_shape] != [row[1:] for row in after_shape]:
            raise ValueError('Thai shaping changed while replacing Latin font')
        checked_thai += len(batch)
    return {'preserved_non_ascii_codepoints': len(preserved_cps),
            'preserved_non_ascii_glyph_outlines': len(preserved_names),
            'native_ascii_runs_checked': checked_ascii, 'native_thai_runs_unchanged': checked_thai,
            'english_shaping': 'MATCHES_DONOR', 'thai_shaping': 'UNCHANGED'}
