"""Build the two current Chinese Word manuals from their Markdown sources.

Requires python-docx. Run with the Codex bundled Python when editing documents.
The checked-in DOCX files are copied by the release packager, so release users
and the normal CI packaging job do not need this authoring dependency.
"""
import os
import re
from pathlib import Path

from docx import Document
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt, RGBColor

ROOT = Path(__file__).resolve().parents[1]
FONT = 'Microsoft YaHei'


def font_style(style, size, bold=False):
    style.font.name = FONT
    style.font.size = Pt(size)
    style.font.bold = bold
    style.font.color.rgb = RGBColor(0, 0, 0)
    rpr = style.element.get_or_add_rPr()
    fonts = rpr.find(qn('w:rFonts'))
    if fonts is None:
        fonts = OxmlElement('w:rFonts')
        rpr.append(fonts)
    for attr in ['ascii', 'hAnsi', 'eastAsia', 'cs']:
        fonts.set(qn('w:' + attr), FONT)
    for attr in ['asciiTheme', 'hAnsiTheme', 'eastAsiaTheme', 'cstheme']:
        fonts.attrib.pop(qn('w:' + attr), None)


def add_text(paragraph, text):
    for part in re.split(r'(\*\*.*?\*\*|`[^`]+`)', text):
        if part.startswith('**') and part.endswith('**'):
            paragraph.add_run(part[2:-2]).bold = True
        elif part.startswith('`') and part.endswith('`'):
            paragraph.add_run(part[1:-1])
        else:
            paragraph.add_run(part)


def field(paragraph, code):
    item = OxmlElement('w:fldSimple')
    item.set(qn('w:instr'), code)
    paragraph._p.append(item)


def table(document, rows):
    parsed = [[c.strip() for c in row.strip().strip('|').split('|')] for row in rows]
    data = [parsed[0]] + [r for r in parsed[1:] if not all(re.fullmatch(r':?-+:?', c) for c in r)]
    t = document.add_table(rows=0, cols=len(data[0]))
    t.alignment = WD_TABLE_ALIGNMENT.CENTER
    t.autofit = False
    widths = [2.0, 4.8] if len(data[0]) == 2 else [6.8 / len(data[0])] * len(data[0])
    for col, width in zip(t.columns, widths):
        col.width = Inches(width)
    props = t._tbl.tblPr
    borders = OxmlElement('w:tblBorders')
    for edge in ['top', 'left', 'bottom', 'right', 'insideH', 'insideV']:
        item = OxmlElement('w:' + edge)
        item.set(qn('w:val'), 'single')
        item.set(qn('w:sz'), '4')
        item.set(qn('w:color'), 'D9D9D9')
        borders.append(item)
    props.append(borders)
    for idx, values in enumerate(data):
        row = t.add_row()
        trpr = row._tr.get_or_add_trPr()
        no_split = OxmlElement('w:cantSplit')
        trpr.append(no_split)
        if idx == 0:
            trpr.append(OxmlElement('w:tblHeader'))
        for col_idx, value in enumerate(values):
            cell = row.cells[col_idx]
            cell.width = Inches(widths[col_idx])
            cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
            tcpr = cell._tc.get_or_add_tcPr()
            margins = OxmlElement('w:tcMar')
            for edge, size in [('top', 65), ('bottom', 65), ('left', 100), ('right', 100)]:
                margin = OxmlElement('w:' + edge)
                margin.set(qn('w:w'), str(size))
                margin.set(qn('w:type'), 'dxa')
                margins.append(margin)
            tcpr.append(margins)
            if idx == 0:
                shade = OxmlElement('w:shd')
                shade.set(qn('w:fill'), 'E7EDF2')
                tcpr.append(shade)
            p = cell.paragraphs[0]
            p.style = document.styles['Table Body']
            p.paragraph_format.keep_with_next = idx == 0
            add_text(p, value)
            if idx == 0:
                for run in p.runs:
                    run.bold = True
    spacer = document.add_paragraph()
    spacer.paragraph_format.space_after = Pt(2)
    spacer.paragraph_format.line_spacing = Pt(3)
    spacer.paragraph_format.space_before = Pt(0)


def build(source):
    doc = Document()
    section = doc.sections[0]
    section.page_width, section.page_height = Inches(8.5), Inches(11)
    section.top_margin = section.bottom_margin = Inches(0.7)
    section.left_margin = section.right_margin = Inches(0.85)
    section.header_distance = section.footer_distance = Inches(0.3)
    normal = doc.styles['Normal']
    font_style(normal, 11)
    normal.paragraph_format.line_spacing = 1.3
    normal.paragraph_format.space_after = Pt(7)
    normal.paragraph_format.widow_control = True
    for name, size in [('Title', 23), ('Heading 1', 16), ('Heading 2', 13)]:
        style = doc.styles[name]
        font_style(style, size, True)
        style.paragraph_format.space_before = Pt(12 if name != 'Title' else 0)
        style.paragraph_format.space_after = Pt(9)
        style.paragraph_format.keep_with_next = True
        if name == 'Title':
            # Word's built-in Title style can carry a themed blue bottom border.
            # Keep a plain black title with no decorative rule in the DOCX.
            ppr = style.element.get_or_add_pPr()
            border = ppr.find(qn('w:pBdr'))
            if border is not None:
                ppr.remove(border)
    for name in ['Header', 'Footer']:
        font_style(doc.styles[name], 9)
    style = doc.styles.add_style('Table Body', 1)
    font_style(style, 10.5)
    style.paragraph_format.line_spacing = 1.2
    style.paragraph_format.space_after = Pt(0)
    style.paragraph_format.space_before = Pt(0)
    hdr = section.header.paragraphs[0]
    hdr.alignment = WD_ALIGN_PARAGRAPH.RIGHT
    hdr.add_run('IdleLegions自动下井  1.4.1')
    footer = section.footer.paragraphs[0]
    footer.alignment = WD_ALIGN_PARAGRAPH.CENTER
    footer.add_run('第 ')
    field(footer, 'PAGE')
    footer.add_run(' 页 / 共 ')
    field(footer, 'NUMPAGES')
    footer.add_run(' 页')
    lines = source.read_text(encoding='utf-8').splitlines()
    idx = 0
    pending_page_break = False
    while idx < len(lines):
        line = lines[idx].strip()
        idx += 1
        if not line:
            continue
        if line == '<!-- pagebreak -->':
            pending_page_break = True
            continue
        if line.startswith('|'):
            rows = [line]
            while idx < len(lines) and lines[idx].strip().startswith('|'):
                rows.append(lines[idx].strip())
                idx += 1
            table(doc, rows)
            continue
        m = re.match(r'^(#{1,3})\s+(.+)$', line)
        if m:
            level = len(m[1])
            p = doc.add_paragraph(m[2], 'Title' if level == 1 else 'Heading ' + str(level - 1))
        else:
            p = doc.add_paragraph()
            add_text(p, line)
            if re.match(r'^\d+\.\s', line):
                p.paragraph_format.left_indent = Inches(0.18)
                p.paragraph_format.first_line_indent = Inches(-0.18)
            elif line.startswith('版本 '):
                p.paragraph_format.space_after = Pt(12)
                for run in p.runs:
                    run.font.size = Pt(10)
        if pending_page_break:
            p.paragraph_format.page_break_before = True
            pending_page_break = False
    doc.core_properties.title = lines[0].lstrip('# ')
    doc.core_properties.subject = 'IdleLegions自动下井1.4.1用户文档'
    doc.core_properties.author = 'IdleLegions-AutoAbyss'
    doc.core_properties.language = 'zh-CN'
    destination = source.with_suffix('.docx')
    temporary = destination.with_suffix('.docx.tmp')
    doc.save(temporary)
    os.replace(temporary, destination)
    print(destination)


if __name__ == '__main__':
    for name in ['使用说明', '使用教程']:
        build(ROOT / 'docs' / (name + '.md'))
