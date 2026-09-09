#!/usr/bin/env python3
"""Generate the fold-v1 character folding table.

Writes three files from one source of truth:
  data/fold-v1.json                                   the contract
  dotnet/src/Vulgarity/Normalization/FoldTable.g.cs   compiled-in C# table
  dart/lib/src/normalization/fold_table.g.dart        compiled-in Dart table

Run:  python3 tool/gen_fold_table.py
Check: python3 tool/gen_fold_table.py --check   (fails if outputs are stale)
"""

import json
import os
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from foldlib import Folder

PROFILE = "fold-v1"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# --------------------------------------------------------------------------
# Soft folds: the source character is punctuation or a digit standing in for a
# letter. It matches like a letter, but it does NOT count as a word character
# when the matcher tests a word boundary.
# --------------------------------------------------------------------------
FOLD_SOFT = {
    "@": "a",
    "4": "a",
    "3": "e",
    "1": "i",
    "!": "i",
    "|": "i",
    "0": "o",
    "$": "s",
    "5": "s",
    "7": "t",
    "+": "t",
    # The fullwidth digits that stand in for a letter fold straight to that
    # letter. Routing them through the fullwidth 0-9 range instead would land
    # on an ASCII digit that folds a second time, so folding would not be
    # idempotent. The four digits with no leetspeak meaning stay in the range.
    "０": "o",  # fullwidth 0
    "１": "i",  # fullwidth 1
    "３": "e",  # fullwidth 3
    "４": "a",  # fullwidth 4
    "５": "s",  # fullwidth 5
    "７": "t",  # fullwidth 7
}

# --------------------------------------------------------------------------
# Hard folds: the source character is a real letter. It counts as a word
# character. Built from three groups below.
# --------------------------------------------------------------------------

# Ligatures and stroked letters that NFD cannot decompose.
SPECIAL = {
    "ß": "ss", "ẞ": "ss",
    "æ": "ae", "Æ": "ae",
    "œ": "oe", "Œ": "oe",
    "ø": "o",  "Ø": "o",
    "đ": "d",  "Đ": "d",
    "ð": "d",  "Ð": "d",
    "þ": "th", "Þ": "th",
    "ł": "l",  "Ł": "l",
    "ŧ": "t",  "Ŧ": "t",
    "ħ": "h",  "Ħ": "h",
    "ı": "i",  "İ": "i",
    "ŋ": "n",  "Ŋ": "n",
    "ĸ": "k",
    "ſ": "s",
    "ƒ": "f",
    "ẚ": "a",
    "ﬀ": "ff", "ﬁ": "fi", "ﬂ": "fl", "ﬃ": "ffi", "ﬄ": "ffl", "ﬅ": "st", "ﬆ": "st",
}

# Cyrillic characters that look like Latin ones.
CYRILLIC = {
    "а": "a", "А": "a",
    "в": "b", "В": "b",
    "с": "c", "С": "c",
    "ԁ": "d",
    "е": "e", "Е": "e", "ё": "e", "Ё": "e",
    "ғ": "f",
    "һ": "h", "Һ": "h", "н": "h", "Н": "h",
    "і": "i", "І": "i",
    "ј": "j", "Ј": "j",
    "к": "k", "К": "k",
    "м": "m", "М": "m",
    "о": "o", "О": "o",
    "р": "p", "Р": "p",
    "ԛ": "q",
    "ѕ": "s", "Ѕ": "s",
    "т": "t", "Т": "t",
    "и": "u",
    "ѵ": "v",
    "ԝ": "w",
    "х": "x", "Х": "x",
    "у": "y", "У": "y",
    "з": "z",
}

# Greek characters that look like Latin ones.
GREEK = {
    "α": "a", "Α": "a",
    "β": "b", "Β": "b",
    "ε": "e", "Ε": "e",
    "η": "n", "Η": "h",
    "ι": "i", "Ι": "i",
    "κ": "k", "Κ": "k",
    "μ": "m", "Μ": "m",
    "ν": "v", "Ν": "n",
    "ο": "o", "Ο": "o",
    "ρ": "p", "Ρ": "p",
    "σ": "s", "ς": "s",
    "τ": "t", "Τ": "t",
    "υ": "u", "Υ": "y",
    "χ": "x", "Χ": "x",
    "Ζ": "z",
}


def build_accents():
    """Decompose accented Latin letters down to their ASCII base."""
    out = {}
    blocks = list(range(0x00C0, 0x0250)) + list(range(0x1E00, 0x1F00))
    for cp in blocks:
        ch = chr(cp)
        if ch in SPECIAL:
            continue
        decomposed = unicodedata.normalize("NFD", ch)
        base = "".join(c for c in decomposed if not unicodedata.combining(c))
        # The base can itself be a ligature, as in a-acute-e for U+01FD.
        base = "".join(SPECIAL.get(c, SPECIAL.get(c.lower(), c)) for c in base)
        base = base.lower()
        if base and base != ch and all("a" <= c <= "z" for c in base):
            out[ch] = base
    return out


# --------------------------------------------------------------------------
# Algorithmic ranges: styled alphabets that map onto a-z by a fixed offset.
# --------------------------------------------------------------------------
def build_ranges():
    r = []

    def add(start, base, note, count=26):
        r.append({"from": start, "to": start + count - 1, "base": ord(base), "note": note})

    add(0xFF21, "a", "fullwidth A-Z")
    add(0xFF41, "a", "fullwidth a-z")
    r.append({"from": 0xFF10, "to": 0xFF19, "base": ord("0"), "note": "fullwidth 0-9"})
    add(0x24B6, "a", "circled A-Z")
    add(0x24D0, "a", "circled a-z")
    add(0x1F130, "a", "squared A-Z")
    add(0x1F150, "a", "negative circled A-Z")
    add(0x1F170, "a", "negative squared A-Z")
    add(0x1F1E6, "a", "regional indicators")

    math_blocks = [
        (0x1D400, "bold A-Z"), (0x1D41A, "bold a-z"),
        (0x1D434, "italic A-Z"), (0x1D44E, "italic a-z"),
        (0x1D468, "bold italic A-Z"), (0x1D482, "bold italic a-z"),
        (0x1D49C, "script A-Z"), (0x1D4B6, "script a-z"),
        (0x1D4D0, "bold script A-Z"), (0x1D4EA, "bold script a-z"),
        (0x1D504, "fraktur A-Z"), (0x1D51E, "fraktur a-z"),
        (0x1D538, "double-struck A-Z"), (0x1D552, "double-struck a-z"),
        (0x1D56C, "bold fraktur A-Z"), (0x1D586, "bold fraktur a-z"),
        (0x1D5A0, "sans A-Z"), (0x1D5BA, "sans a-z"),
        (0x1D5D4, "sans bold A-Z"), (0x1D5EE, "sans bold a-z"),
        (0x1D608, "sans italic A-Z"), (0x1D622, "sans italic a-z"),
        (0x1D63C, "sans bold italic A-Z"), (0x1D656, "sans bold italic a-z"),
        (0x1D670, "monospace A-Z"), (0x1D68A, "monospace a-z"),
    ]
    for start, note in math_blocks:
        add(start, "a", "math " + note)
    return r


# --------------------------------------------------------------------------
# Drop ranges. dropBreak ends a word; dropSilent does not.
# A fold-table entry always wins over a drop range.
#
# The two sets MUST come out disjoint, or a port that tests them in a different
# order reaches a different answer. An invisible character inside a word once
# landed in a dropBreak range, which forged a word boundary and let "cl<SHY>ass"
# report "ass". So the broad dropBreak blocks are written here as they read,
# and every dropSilent code point is then subtracted from them below.
# --------------------------------------------------------------------------
DROP_BREAK_RAW = [
    [0x0000, 0x002F],  # controls, space, ASCII punctuation up to /
    [0x003A, 0x0040],  # : ; < = > ? @
    [0x005B, 0x0060],  # [ \ ] ^ _ `
    [0x007B, 0x00BF],  # { | } ~ DEL, Latin-1 controls and punctuation
    [0x00D7, 0x00D7],  # multiplication sign
    [0x00F7, 0x00F7],  # division sign
    [0x02B0, 0x02FF],  # spacing modifier letters
    [0x0374, 0x0375],  # Greek numeral signs
    [0x037E, 0x037E],  # Greek question mark
    [0x0384, 0x0385],  # Greek accents
    [0x0387, 0x0387],  # Greek ano teleia
    [0x055A, 0x055F],  # Armenian punctuation
    [0x058A, 0x058A],  # Armenian hyphen
    [0x05BE, 0x05C7],  # Hebrew punctuation
    [0x060C, 0x060C],  # Arabic comma
    [0x061B, 0x061F],  # Arabic semicolon and question mark
    [0x066A, 0x066D],  # Arabic punctuation
    [0x2000, 0x206F],  # general punctuation, dashes, quotes, bullets
    [0x2070, 0x209F],  # super and subscripts
    [0x20A0, 0x20CF],  # currency symbols
    [0x2100, 0x214F],  # letterlike symbols
    [0x2150, 0x218F],  # number forms
    [0x2190, 0x21FF],  # arrows
    [0x2200, 0x22FF],  # mathematical operators
    [0x2300, 0x23FF],  # miscellaneous technical
    [0x2400, 0x243F],  # control pictures
    [0x2500, 0x257F],  # box drawing
    [0x2580, 0x259F],  # block elements
    [0x25A0, 0x25FF],  # geometric shapes
    [0x2600, 0x27BF],  # miscellaneous symbols and dingbats
    [0x2900, 0x297F],  # supplemental arrows
    [0x2E00, 0x2E7F],  # supplemental punctuation
    [0x3000, 0x303F],  # CJK symbols and punctuation
    [0xFE30, 0xFE6F],  # CJK compatibility forms, small forms
    [0xFF01, 0xFF0F],  # fullwidth punctuation
    [0xFF1A, 0xFF20],  # fullwidth punctuation
    [0xFF3B, 0xFF40],  # fullwidth punctuation
    [0xFF5B, 0xFF65],  # fullwidth punctuation
    [0x1F300, 0x1F5FF],  # miscellaneous symbols and pictographs
    [0x1F600, 0x1F64F],  # emoticons
    [0x1F680, 0x1F6FF],  # transport and map symbols
    [0x1F900, 0x1F9FF],  # supplemental symbols and pictographs
    [0x1FA00, 0x1FAFF],  # symbols and pictographs extended-A
]

DROP_SILENT = [
    [0x00AD, 0x00AD],  # soft hyphen
    [0x0300, 0x036F],  # combining diacritical marks
    [0x0483, 0x0489],  # combining Cyrillic marks
    [0x0591, 0x05BD],  # Hebrew points
    [0x0610, 0x061A],  # Arabic marks
    [0x064B, 0x065F],  # Arabic diacritics
    [0x0670, 0x0670],  # Arabic superscript alef
    [0x1AB0, 0x1AFF],  # combining diacritical marks extended
    [0x1DC0, 0x1DFF],  # combining diacritical marks supplement
    [0x200B, 0x200F],  # zero width space, ZWNJ, ZWJ, directional marks
    [0x202A, 0x202E],  # directional embedding and override
    [0x2060, 0x2064],  # word joiner, invisible operators
    [0x206A, 0x206F],  # deprecated format characters
    [0x20D0, 0x20FF],  # combining marks for symbols
    [0xFE00, 0xFE0F],  # variation selectors
    [0xFE20, 0xFE2F],  # combining half marks
    [0xFEFF, 0xFEFF],  # zero width no-break space
    [0xFFF9, 0xFFFB],  # interlinear annotation
    [0xE0000, 0xE01EF],  # tags and variation selectors supplement
]


def subtract_ranges(ranges, holes):
    """Remove every code point covered by holes from ranges.

    Keeps the order of the surviving pieces, so the emitted table still reads
    from low code point to high.
    """
    out = []
    for lo, hi in ranges:
        pieces = [(lo, hi)]
        for hole_lo, hole_hi in holes:
            split = []
            for a, b in pieces:
                if hole_hi < a or hole_lo > b:
                    split.append((a, b))
                    continue
                if a < hole_lo:
                    split.append((a, hole_lo - 1))
                if b > hole_hi:
                    split.append((hole_hi + 1, b))
            pieces = split
        out.extend(pieces)
    return [[a, b] for a, b in out]


# dropBreak with every dropSilent code point carved out of it.
DROP_BREAK = subtract_ranges(DROP_BREAK_RAW, DROP_SILENT)


def assert_ranges_disjoint(drop_break, drop_silent):
    """Fail generation when a code point sits in both drop sets.

    classify() tests the two sets in a fixed order, and the ports are free to
    pick their own order. Disjoint ranges are what makes that safe.
    """
    for lo, hi in drop_break:
        if lo > hi:
            raise SystemExit("dropBreak range runs backwards: %04X..%04X" % (lo, hi))
        for slo, shi in drop_silent:
            if lo <= shi and slo <= hi:
                raise SystemExit(
                    "dropBreak %04X..%04X overlaps dropSilent %04X..%04X. "
                    "Add the silent range to DROP_SILENT and let subtract_ranges "
                    "carve it out of DROP_BREAK_RAW." % (lo, hi, slo, shi)
                )


def assert_idempotent(doc):
    """Fail generation when folding a folded character folds again.

    Both ports fold terms at build time and text at scan time. A character that
    keeps changing would make a folded term unreachable, so the whole table has
    to reach a fixed point in one pass.
    """
    folder = Folder(doc=doc)
    for cp in range(0x20000):
        if 0xD800 <= cp <= 0xDFFF:
            continue  # a lone surrogate is not a character
        once = folder.fold(chr(cp))
        twice = folder.fold(once)
        if once != twice:
            raise SystemExit(
                "fold is not idempotent at U+%04X: %r folds again to %r"
                % (cp, once, twice)
            )


def build_document():
    hard = {}
    hard.update(build_accents())
    hard.update(SPECIAL)
    hard.update(CYRILLIC)
    hard.update(GREEK)

    # A character must not sit in both maps.
    overlap = set(hard) & set(FOLD_SOFT)
    if overlap:
        raise SystemExit("character in both fold maps: %r" % sorted(overlap))

    assert_ranges_disjoint(DROP_BREAK, DROP_SILENT)

    doc = {
        "profile": PROFILE,
        "note": (
            "Character folding contract for the vulgarity matcher. "
            "Order of operations per code point: foldSoft, foldHard, ranges, "
            "ASCII a-z 0-9 passthrough, A-Z lowercase, dropBreak, dropSilent, "
            "then emit unchanged. A fold entry always wins over a drop range. "
            "dropBreak and dropSilent are disjoint, so the order in which a "
            "port tests them cannot change the answer. Folding is idempotent: "
            "folding a folded character leaves it alone."
        ),
        "foldSoft": dict(sorted(FOLD_SOFT.items())),
        "foldHard": dict(sorted(hard.items())),
        "ranges": build_ranges(),
        "dropBreak": DROP_BREAK,
        "dropSilent": DROP_SILENT,
    }

    assert_idempotent(doc)
    return doc


# --------------------------------------------------------------------------
# Emitters
# --------------------------------------------------------------------------
BANNER = "// GENERATED by tool/gen_fold_table.py from data/fold-v1.json. Do not edit."


def esc_cs(s):
    return "".join("\\u%04X" % ord(c) if (ord(c) > 126 or ord(c) < 32 or c in '"\\') else c for c in s)


def cp_literal(cp):
    return "0x%04X" % cp


def emit_csharp(doc):
    lines = [BANNER, "", "using System.Collections.Generic;", "",
             "namespace Vulgarity.Normalization", "{",
             "    /// <summary>The fold-v1 character folding tables.</summary>",
             "    internal static partial class FoldTableData", "    {",
             '        public const string Profile = "%s";' % doc["profile"], ""]

    lines.append("        public static readonly Dictionary<int, string> Soft = new Dictionary<int, string>")
    lines.append("        {")
    for k, v in doc["foldSoft"].items():
        lines.append('            { %s, "%s" },' % (cp_literal(ord(k)), esc_cs(v)))
    lines.append("        };")
    lines.append("")

    lines.append("        public static readonly Dictionary<int, string> Hard = new Dictionary<int, string>")
    lines.append("        {")
    for k, v in doc["foldHard"].items():
        lines.append('            { %s, "%s" },' % (cp_literal(ord(k)), esc_cs(v)))
    lines.append("        };")
    lines.append("")

    lines.append("        // from, to, base")
    lines.append("        public static readonly int[][] Ranges =")
    lines.append("        {")
    for r in doc["ranges"]:
        lines.append("            new[] { %s, %s, %s }, // %s"
                     % (cp_literal(r["from"]), cp_literal(r["to"]), cp_literal(r["base"]), r["note"]))
    lines.append("        };")
    lines.append("")

    for name, key in (("DropBreak", "dropBreak"), ("DropSilent", "dropSilent")):
        lines.append("        public static readonly int[][] %s =" % name)
        lines.append("        {")
        for lo, hi in doc[key]:
            lines.append("            new[] { %s, %s }," % (cp_literal(lo), cp_literal(hi)))
        lines.append("        };")
        lines.append("")

    lines.append("    }")
    lines.append("}")
    return "\n".join(lines) + "\n"


def esc_dart(s):
    return "".join("\\u{%X}" % ord(c) if (ord(c) > 126 or ord(c) < 32 or c in "'\\$") else c for c in s)


def emit_dart(doc):
    lines = [BANNER, "", "/// The fold-v1 character folding tables.", "library;", "",
             "const String kFoldProfile = '%s';" % doc["profile"], ""]

    lines.append("const Map<int, String> kFoldSoft = <int, String>{")
    for k, v in doc["foldSoft"].items():
        lines.append("  %s: '%s'," % (cp_literal(ord(k)), esc_dart(v)))
    lines.append("};")
    lines.append("")

    lines.append("const Map<int, String> kFoldHard = <int, String>{")
    for k, v in doc["foldHard"].items():
        lines.append("  %s: '%s'," % (cp_literal(ord(k)), esc_dart(v)))
    lines.append("};")
    lines.append("")

    lines.append("/// Each entry is [from, to, base].")
    lines.append("const List<List<int>> kFoldRanges = <List<int>>[")
    for r in doc["ranges"]:
        lines.append("  <int>[%s, %s, %s], // %s"
                     % (cp_literal(r["from"]), cp_literal(r["to"]), cp_literal(r["base"]), r["note"]))
    lines.append("];")
    lines.append("")

    for name, key in (("kDropBreak", "dropBreak"), ("kDropSilent", "dropSilent")):
        lines.append("const List<List<int>> %s = <List<int>>[" % name)
        for lo, hi in doc[key]:
            lines.append("  <int>[%s, %s]," % (cp_literal(lo), cp_literal(hi)))
        lines.append("];")
        lines.append("")

    return "\n".join(lines)


def main():
    check = "--check" in sys.argv
    doc = build_document()

    targets = {
        os.path.join(ROOT, "data", "fold-v1.json"):
            json.dumps(doc, ensure_ascii=False, indent=2) + "\n",
        os.path.join(ROOT, "dotnet", "src", "Vulgarity", "Normalization", "FoldTable.g.cs"):
            emit_csharp(doc),
        os.path.join(ROOT, "dart", "lib", "src", "normalization", "fold_table.g.dart"):
            emit_dart(doc),
    }

    stale = []
    for path, content in targets.items():
        if check:
            current = open(path, encoding="utf-8").read() if os.path.exists(path) else None
            if current != content:
                stale.append(os.path.relpath(path, ROOT))
        else:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "w", encoding="utf-8") as fh:
                fh.write(content)

    if check:
        if stale:
            print("STALE: " + ", ".join(stale))
            return 1
        print("fold table is current")
        return 0

    print("soft folds: %d" % len(doc["foldSoft"]))
    print("hard folds: %d" % len(doc["foldHard"]))
    print("ranges:     %d" % len(doc["ranges"]))
    for path in targets:
        print("wrote %s" % os.path.relpath(path, ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
