"""Append Thai glyphs/layout to cooked MiSans faces; preserve original glyph IDs.

Only reads byte strings. Callers decide where to save the resulting trial assets.
Original TrueType programs and CFF outlines remain in their own font; the added
TrueType glyphs are unhinted, so they never use the original face's CVT program.
"""
import copy
import io
import re
import struct
import xml.etree.ElementTree as ET

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.pens.t2CharStringPen import T2CharStringPen


def _rename(font, mapping):
    """Round-trip donor glyph names through the documented fontTools XML format."""
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


def _shift_nested_lookups(value, offset):
    # Contextual lookups refer to other lookups by index, independently of features.
    if isinstance(value, list):
        for item in value:
            _shift_nested_lookups(item, offset)
    elif hasattr(value, '__dict__'):
        for name, item in vars(value).items():
            if name == 'LookupListIndex' and isinstance(item, int):
                setattr(value, name, item + offset)
            else:
                _shift_nested_lookups(item, offset)


def _merge_gdef(original, donor):
    target, incoming = original['GDEF'].table, donor['GDEF'].table
    for name in ['GlyphClassDef', 'MarkAttachClassDef']:
        source = getattr(incoming, name, None)
        if source is None:
            continue
        current = getattr(target, name, None)
        if current is None:
            setattr(target, name, copy.deepcopy(source))
        else:
            for glyph, value in source.classDefs.items():
                if glyph == '.notdef':
                    continue
                if glyph in current.classDefs:
                    raise ValueError('Donor GDEF glyph overlaps original: ' + glyph)
                current.classDefs[glyph] = value
    sets = getattr(incoming, 'MarkGlyphSetsDef', None)
    if sets is None:
        return 0
    old_sets = getattr(target, 'MarkGlyphSetsDef', None)
    offset = len(old_sets.Coverage) if old_sets else 0
    if old_sets:
        old_sets.Coverage.extend(copy.deepcopy(sets.Coverage))
        old_sets.MarkSetCount = len(old_sets.Coverage)
    else:
        target.MarkGlyphSetsDef = copy.deepcopy(sets)
    target.Version = max(target.Version, 0x00010002)
    return offset


def _merge_layout(original, donor, tag, mark_set_offset):
    target, incoming = original[tag].table, donor[tag].table
    lookup_offset = len(target.LookupList.Lookup)
    incoming_lookups = copy.deepcopy(incoming.LookupList.Lookup)
    for lookup in incoming_lookups:
        _shift_nested_lookups(lookup, lookup_offset)
        if lookup.LookupFlag & 0x0010:
            lookup.MarkFilteringSet += mark_set_offset
    target.LookupList.Lookup.extend(incoming_lookups)
    target.LookupList.LookupCount = len(target.LookupList.Lookup)
    feature_offset = len(target.FeatureList.FeatureRecord)
    incoming_features = copy.deepcopy(incoming.FeatureList.FeatureRecord)
    for record in incoming_features:
        record.Feature.LookupListIndex = [i + lookup_offset for i in record.Feature.LookupListIndex]
    target.FeatureList.FeatureRecord.extend(incoming_features)
    script = copy.deepcopy(next(r for r in incoming.ScriptList.ScriptRecord if r.ScriptTag == 'thai'))
    if any(r.ScriptTag == 'thai' for r in target.ScriptList.ScriptRecord):
        raise ValueError('Original already has Thai layout; refusing duplicate script')
    for language in [script.Script.DefaultLangSys] + [r.LangSys for r in script.Script.LangSysRecord]:
        if language is None:
            continue
        language.FeatureIndex = [i + feature_offset for i in language.FeatureIndex]
        if language.ReqFeatureIndex != 0xFFFF:
            language.ReqFeatureIndex += feature_offset
    target.ScriptList.ScriptRecord.append(script)
    target.ScriptList.ScriptRecord.sort(key=lambda r: r.ScriptTag)
    target.ScriptList.ScriptCount = len(target.ScriptList.ScriptRecord)
    # OpenType requires feature records sorted by tag. Remap every original
    # language's feature indices too, preserving its previous lookup semantics.
    indexed = sorted(enumerate(target.FeatureList.FeatureRecord), key=lambda pair: pair[1].FeatureTag)
    index_map = {old: new for new, (old, _) in enumerate(indexed)}
    target.FeatureList.FeatureRecord = [record for _, record in indexed]
    target.FeatureList.FeatureCount = len(indexed)
    for record in target.ScriptList.ScriptRecord:
        for language in [record.Script.DefaultLangSys] + [r.LangSys for r in record.Script.LangSysRecord]:
            if language is None:
                continue
            language.FeatureIndex = [index_map[i] for i in language.FeatureIndex]
            if language.ReqFeatureIndex != 0xFFFF:
                language.ReqFeatureIndex = index_map[language.ReqFeatureIndex]


def augment_ufont(original, donor):
    size = struct.unpack_from('<I', original)[0]
    if size != len(original) - 8 or original[-4:] != bytes(4):
        raise ValueError('Unexpected cooked font wrapper')
    target = TTFont(io.BytesIO(original[4:4 + size]))
    source = TTFont(io.BytesIO(donor))
    target.ensureDecompiled()
    if target['head'].unitsPerEm != source['head'].unitsPerEm:
        raise ValueError('Font units differ; no implicit scaling is permitted')
    missing = [cp for cp in source.getBestCmap()
               if 0x0E01 <= cp <= 0x0E5B and cp not in target.getBestCmap()]
    if not missing:
        raise ValueError('Original font already contains the donor Thai range')
    options = subset.Options()
    options.hinting = False
    options.drop_tables += ['TTFA']
    selector = subset.Subsetter(options=options)
    selector.populate(unicodes=missing)
    selector.subset(source)
    order = list(target.getGlyphOrder())
    added = [name for name in source.getGlyphOrder() if name != '.notdef']
    if 'CFF ' in target:
        top = target['CFF '].cff.topDictIndex[0]
        if top.ROS != ('Adobe', 'Identity', 0) or len(top.FDArray) != 1:
            raise ValueError('Unsupported CFF font dictionary structure')
        mapping = {name: 'cid%05d' % (len(order) + i) for i, name in enumerate(added)}
        source = _rename(source, mapping)
        added = [mapping[name] for name in added]
        glyph_set = source.getGlyphSet()
        private = top.FDArray[0].Private
        strings = top.CharStrings
        for name in added:
            advance = source['hmtx'].metrics[name][0]
            width = None if advance == private.defaultWidthX else advance - private.nominalWidthX
            pen = T2CharStringPen(width, glyph_set)
            glyph_set[name].draw(pen)
            cs = pen.getCharString(private=private, globalSubrs=target['CFF '].cff.GlobalSubrs)
            strings.charStrings[name] = len(strings.charStringsIndex)
            strings.charStringsIndex.append(cs)
            top.charset.append(name)
            top.FDSelect.append(0)
        top.CIDCount = len(order) + len(added)
    elif 'glyf' in target:
        for name in added:
            target['glyf'].glyphs[name] = copy.deepcopy(source['glyf'][name])
    else:
        raise ValueError('Unsupported outline format')
    if set(order) & set(added):
        raise ValueError('Appended glyph names overlap original')
    target.setGlyphOrder(order + added)
    target['maxp'].numGlyphs = len(order) + len(added)
    for name in added:
        target['hmtx'].metrics[name] = source['hmtx'].metrics[name]
        if 'vmtx' in target:
            # The donor has horizontal metrics only. Existing vertical entries
            # stay intact; appended glyphs use the face's default vertical cell.
            target['vmtx'].metrics[name] = target['vmtx'].metrics['.notdef']
    for table in target['cmap'].tables:
        if table.isUnicode() and table.format in (4, 12):
            table.cmap.update(source.getBestCmap())
    mark_set_offset = _merge_gdef(target, source)
    for tag in ['GSUB', 'GPOS']:
        _merge_layout(target, source, tag, mark_set_offset)
    # Windows clipping extents may grow for Thai marks; baseline and line spacing
    # stay exactly as in the original MiSans face.
    for attribute in ['usWinAscent', 'usWinDescent']:
        setattr(target['OS/2'], attribute, max(getattr(target['OS/2'], attribute), getattr(source['OS/2'], attribute)))
    if 'DSIG' in target:
        del target['DSIG']
    stream = io.BytesIO()
    target.save(stream)
    raw = stream.getvalue()
    return struct.pack('<I', len(raw)) + raw + original[-4:]


def verify_ufont(original, revised, donor):
    """Check glyph preservation and Thai shaping against the installed donor."""
    import uharfbuzz as hb
    from fontTools.pens.recordingPen import RecordingPen

    def unwrap(raw):
        n = struct.unpack_from('<I', raw)[0]
        if n != len(raw) - 8 or raw[-4:] != bytes(4):
            raise ValueError('Invalid cooked font length or trailer')
        return raw[4:-4]

    def shape(raw, text, script=None):
        font = hb.Font(hb.Face(raw))
        buffer = hb.Buffer()
        buffer.add_str(text)
        buffer.guess_segment_properties()
        if script:
            buffer.script = script
        hb.shape(font, buffer)
        return [(i.codepoint, i.cluster, p.x_advance, p.x_offset, p.y_offset)
                for i, p in zip(buffer.glyph_infos, buffer.glyph_positions)]

    before_raw, after_raw = unwrap(original), unwrap(revised)
    before, after = TTFont(io.BytesIO(before_raw)), TTFont(io.BytesIO(after_raw))
    old_map, new_map = before.getBestCmap(), after.getBestCmap()
    for cp, name in old_map.items():
        if cp not in new_map or before.getGlyphID(name) != after.getGlyphID(new_map[cp]):
            raise ValueError('Original character mapping changed: ' + hex(cp))
        if before['hmtx'].metrics[name] != after['hmtx'].metrics[new_map[cp]]:
            raise ValueError('Original character metrics changed: ' + hex(cp))
    for text in ["1-0 : Owner's Selection 2,000 ×!?", 'Management Target Support Employee']:
        if shape(before_raw, text) != shape(after_raw, text):
            raise ValueError('Original English shaping changed')
    donor_map = TTFont(io.BytesIO(donor)).getBestCmap()
    thai = [cp for cp in donor_map if 0x0E01 <= cp <= 0x0E5B]
    glyphs = after.getGlyphSet()
    for cp in thai:
        if cp not in new_map:
            raise ValueError('Thai cmap missing: ' + hex(cp))
        pen = RecordingPen()
        glyphs[new_map[cp]].draw(pen)
        if not pen.value:
            raise ValueError('Thai outline empty: ' + hex(cp))
    samples = ['กิ่กำกุ้ง', 'น้ำ', 'กี่', 'ชื่อด่าน', 'เงื่อนไขดาว', 'ร้านค้า',
               'เปิดร้าน', 'ได้รับ', 'ครั้ง', 'ค่าใช้จ่าย', 'เก็บครบ', 'กำลังดำเนินการ']
    for text in samples:
        reference = shape(donor, text, 'Thai')
        result = shape(after_raw, text, 'Thai')
        if any(row[0] == 0 for row in result) or [r[1:] for r in result] != [r[1:] for r in reference]:
            raise ValueError('Thai glyph or mark positioning differs from donor: ' + text)
    if any(getattr(before['hhea'], n) != getattr(after['hhea'], n)
           for n in ['ascent', 'descent', 'lineGap']):
        raise ValueError('Original baseline/line metrics changed')
    return dict(status='PASS_OFFLINE_GLYPHS_AND_SHAPING', original_codepoints_preserved=len(old_map),
                thai_codepoints=len(thai), added_codepoints=len(set(new_map)-set(old_map)),
                added_glyphs=len(after.getGlyphOrder())-len(before.getGlyphOrder()),
                thai_shaping_samples=len(samples), english_shaping='UNCHANGED',
                original_baseline_and_line_spacing='UNCHANGED', runtime_rendering='NOT_TESTED')
