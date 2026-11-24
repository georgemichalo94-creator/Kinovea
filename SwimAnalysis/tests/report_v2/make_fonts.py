# make_fonts.py (Swim 2.38.0): the faces of Inter that the pages of the report of the 2nd version use (Inter 400, 500, 600, 700, 800
# and Inter Display 500, 600, 800), cut to the letters a report can have (Latin, Greek, the punctuation, arrows and signs of the
# numbers) with the features a browser uses by itself and the two the pages ask for (tnum, cv11), with TrueType outlines (the PDF of
# Chromium keeps a TrueType font as a font; a CFF one becomes Type 3 glyphs) as WOFF: Kinovea puts them in the pages it prints with
# Edge, so the PDF is the same on every computer (Inter is not a font of Windows). Inter: SIL Open Font License 1.1 (OFL.txt beside
# the files).
#   python3 make_fonts.py <folder of Inter-*.otf> <out folder>
import os
import sys
from fontTools import subset
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont, newTable

SRC = sys.argv[1] if len(sys.argv) > 1 else '/usr/share/fonts/opentype/inter'
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), '../../../Kinovea.ScreenManager/SwimAnalysis/Fonts')

FACES = [
    ('Inter-Regular', 'Inter', 400), ('Inter-Medium', 'Inter', 500), ('Inter-SemiBold', 'Inter', 600),
    ('Inter-Bold', 'Inter', 700), ('Inter-ExtraBold', 'Inter', 800),
    ('InterDisplay-Medium', 'Inter Display', 500), ('InterDisplay-SemiBold', 'Inter Display', 600),
    ('InterDisplay-ExtraBold', 'Inter Display', 800),
]

# The letters: Latin with its accents (and the Romanian Ș Ț), Greek, the punctuation, the signs of the numbers, a few arrows
# and shapes. The features HarfBuzz applies by itself to Latin and Greek, and the two of the pages (font-variant-numeric: tabular-nums; cv11).
FEATURES = ['ccmp', 'locl', 'mark', 'mkmk', 'rlig', 'calt', 'clig', 'curs', 'dist', 'kern', 'liga', 'rclt', 'rvrn', 'abvm', 'blwm',
            'tnum', 'cv11']

RANGES = [(0x20, 0x7E), (0xA0, 0x17F), (0x218, 0x21B), (0x300, 0x36F), (0x370, 0x3FF), (0x2010, 0x2027), (0x2030, 0x203A),
          (0x2044, 0x2044), (0x2070, 0x2079), (0x2080, 0x2089), (0x20AC, 0x20AC), (0x2116, 0x2116), (0x2122, 0x2122),
          (0x2190, 0x2199), (0x21D2, 0x21D2), (0x21D4, 0x21D4), (0x2212, 0x2212), (0x2215, 0x2215), (0x2219, 0x2219),
          (0x221E, 0x221E), (0x2248, 0x2248), (0x2260, 0x2260), (0x2264, 0x2265), (0x25A0, 0x25A1), (0x25B2, 0x25B3),
          (0x25B6, 0x25B6), (0x25BA, 0x25BA), (0x25BC, 0x25BD), (0x25C0, 0x25C0), (0x25C4, 0x25C4), (0x25C6, 0x25C7),
          (0x25CB, 0x25CB), (0x25CF, 0x25CF), (0x2713, 0x2714), (0x2717, 0x2718)]


def to_truetype(font):
    """The CFF outlines as TrueType ones (quadratic, within 1 unit of 2048 of the em), as the otf2ttf of fontTools."""
    order = font.getGlyphOrder()
    glyphs = font.getGlyphSet()
    quads = {}
    for name in order:
        pen = TTGlyphPen(glyphs)
        glyphs[name].draw(Cu2QuPen(pen, 1.0, reverse_direction=True))
        quads[name] = pen.glyph()
    font['loca'] = newTable('loca')
    font['glyf'] = glyf = newTable('glyf')
    glyf.glyphOrder = order
    glyf.glyphs = quads
    del font['CFF ']
    if 'VORG' in font:
        del font['VORG']
    glyf.compile(font)
    hmtx = font['hmtx']
    for name, g in quads.items():
        width = hmtx[name][0]
        hmtx[name] = (width, getattr(g, 'xMin', 0) if hasattr(g, 'xMin') else 0)
    font['maxp'] = maxp = newTable('maxp')
    maxp.tableVersion = 0x00010000
    maxp.maxZones = 1
    maxp.maxTwilightPoints = 0
    maxp.maxStorage = 0
    maxp.maxFunctionDefs = 0
    maxp.maxInstructionDefs = 0
    maxp.maxStackElements = 0
    maxp.maxSizeOfInstructions = 0
    maxp.maxComponentElements = 0
    post = font['post']
    post.formatType = 2.0
    post.extraNames = []
    post.mapping = {}
    post.glyphOrder = order
    font.sfntVersion = '\x00\x01\x00\x00'


def main():
    os.makedirs(OUT, exist_ok=True)
    unicodes = [u for a, b in RANGES for u in range(a, b + 1)]
    for name, family, weight in FACES:
        options = subset.Options()
        options.layout_features = FEATURES
        options.name_IDs = ['*']
        options.name_languages = ['*']
        options.hinting = False
        options.notdef_outline = True
        options.flavor = 'woff'
        options.with_zopfli = False
        font = TTFont(os.path.join(SRC, name + '.otf'))
        sub = subset.Subsetter(options=options)
        sub.populate(unicodes=unicodes)
        sub.subset(font)
        to_truetype(font)
        target = os.path.join(OUT, name + '.woff')
        font.flavor = 'woff'
        font.save(target)
        print('%-24s %-14s %d  %6d bytes  %d glyphs' % (name, family, weight, os.path.getsize(target), len(font.getGlyphOrder())))


if __name__ == '__main__':
    main()
