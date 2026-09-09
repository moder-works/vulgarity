#!/usr/bin/env python3
"""Find readable vulgarity in the files this project publishes.

Why two passes. Neither search alone finds everything:

  * A RAW search over the literal text finds "the rapist", but misses every
    evasion spelling, such as "f.u.c.k" or "a$$hole".
  * A FOLDED search, through the same fold-v1 profile the matcher uses, finds
    those evasions, but misses "the rapist": it folds to "therapist", and the
    word-boundary rule then clears it. A human reading the file still sees it.

So this reports the union of both.

Severity 1 is allowed by default. The documentation has to name a real term to
be truthful, and the mechanism examples use "damn", "hell" and "crap" for
exactly that. Pass --min-severity 1 to report those too.

Run:   python3 tool/find_plain_terms.py
Check: python3 tool/find_plain_terms.py --check
"""

import argparse
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from foldlib import Folder, squeeze

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# What a reader of the published package can see. data/ holds the authored
# lists and is meant to be readable. .github/ never ships.
#
# pubspec.yaml and LICENSE are in the archive too, and pub.dev renders the
# description straight onto the package page, so they are as public as the
# README.
TARGETS = [
    "README.md",
    "dart/README.md",
    "dart/CHANGELOG.md",
    "dart/pubspec.yaml",
    "dart/LICENSE",
    "dart/lib",
    "dart/example",
    "dotnet/src",
    "dotnet/example",
]

SUFFIXES = (".md", ".dart", ".cs")

# The shortest term worth reporting. Three letters is the floor: "ass" and
# "fag" are terms a reader can see, and stopping at four missed them.
MIN_LENGTH = 3

# Below this length a term is only ever reported as a whole word, whatever its
# own boundary flag says. A three-letter run turns up by chance inside a longer
# word once the fold has dropped the punctuation, and a hit nobody can read on
# the page is not a leak. The seed already flags almost every short term as
# whole-word; this makes the rule hold for the rest of them too.
WHOLE_WORD_LENGTH = 4

# Files that are code. Everywhere else, the whole line is prose.
SOURCE_SUFFIXES = (".dart", ".cs")

# Terms a published file names on purpose, per file.
#
# Dropping the floor to three letters made one real hit visible: README.md
# names a severity-2 three-letter term six times, in code spans, in the section
# that documents the word-boundary rule. That rule is only legible with a term
# that has an innocent host word -- `an ass` flags, `bass` and `assassin` do
# not -- and no severity-1 term has such a neighbour, so "damn", "hell" and
# "crap" cannot stand in for it. Every hit is a deliberate citation, none is a
# leak, and the alternative is a section that cannot say what it means.
#
# This is deliberately per file AND per term. The same term in any other
# published file still fails, and any other term in README.md still fails.
DOCUMENTED = {
    "README.md": frozenset(["ass"]),
}


def load_terms(min_severity):
    """Return the English terms at or above a severity, longest first."""
    doc = json.load(open(os.path.join(ROOT, "data", "seed.json"), encoding="utf-8"))
    allow = set(doc.get("allow", []))
    terms = []
    for entry in doc["entries"]:
        term = entry["t"]
        if entry.get("sev", 1) < min_severity or len(term) < MIN_LENGTH:
            continue
        boundary = bool(entry.get("w")) or len(term) < WHOLE_WORD_LENGTH
        terms.append((term, boundary))
    terms.sort(key=lambda pair: len(pair[0]), reverse=True)
    return terms, allow


def files():
    for target in TARGETS:
        path = os.path.join(ROOT, target)
        if os.path.isfile(path):
            yield path
            continue
        for base, dirs, names in os.walk(path):
            dirs[:] = [d for d in dirs if d not in ("bin", "obj", ".dart_tool")]
            for name in sorted(names):
                if name.endswith(SUFFIXES):
                    yield os.path.join(base, name)


def allowed(term, folded, allow):
    """True when an allowlist word covers this hit.

    The allowlist works against the squeezed stream, so "shiite" is what clears
    the term that "shiite" collapses to. Compare against the squeeze of the
    allowlist word, not the word itself.
    """
    for word in allow:
        if word in folded and (term in word or term in squeeze(word)):
            return True
    return False


def whole_word(text, term):
    return re.search(r"(?<![a-z0-9])" + re.escape(term) + r"(?![a-z0-9])", text)


# A generated pack constant is one long base64 run. Folding it finds four-letter
# terms by chance, which say nothing about what a reader can see.
BASE64_LINE = re.compile(r"^\s*'[A-Za-z0-9+/=]{64,}';?$")

COMMENT = re.compile(r"//+(.*)$|/\*(.*?)\*/", re.S)
LITERAL = re.compile(r"'([^'\n]*)'|\"([^\"\n]*)\"")

# String interpolation is code, not prose, and the fold turns "$" into "s". So
# Dart's "${hit.start}" folds to "shitstart" and reports a term nobody can see.
INTERPOLATION = re.compile(r"\$\{[^}]*\}|\$[A-Za-z_][A-Za-z0-9_]*|\{[0-9]+\}")


def prose(line, source):
    """Return the part of a line a human reads as text, not as code.

    Folding a whole line of source is too noisy to be useful. The fold drops
    punctuation, so `${hit.start}` becomes `hitstart` and joins into a term that
    nobody can see on the page. Comments and string literals are the only places
    a reader meets prose, so fold only those.

    Everything that is not source is read straight through: Markdown, the
    pubspec and the licence are prose from the first character to the last.
    """
    if not source:
        return line
    parts = []
    for groups in COMMENT.findall(line):
        parts.extend(g for g in groups if g)
    for groups in LITERAL.findall(line):
        parts.extend(INTERPOLATION.sub(" ", g) for g in groups if g)
    return " ".join(parts)


def scan(path, terms, allow, folder):
    """Return {line number: set of terms} for one file."""
    found = {}
    source = path.endswith(SOURCE_SUFFIXES)
    with open(path, encoding="utf-8", errors="ignore") as fh:
        lines = fh.read().split("\n")

    for number, line in enumerate(lines, 1):
        if BASE64_LINE.match(line):
            continue

        raw = line.lower()
        folded = folder.fold(prose(line, source))
        streams = (folded, squeeze(folded))

        hits = set()
        for term, boundary in terms:
            # Pass one: the text as a human reads it.
            if whole_word(raw, term):
                hits.add(term)
                continue
            # Pass two: the text as the matcher sees it.
            for stream in streams:
                found_here = whole_word(stream, term) if boundary else term in stream
                if found_here and not allowed(term, folded, allow):
                    hits.add(term)
                    break

        if hits:
            found[number] = hits
    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--check", action="store_true", help="exit 1 on any hit")
    parser.add_argument("--min-severity", type=int, default=2,
                        help="lowest severity to report (default 2)")
    args = parser.parse_args()

    terms, allow = load_terms(args.min_severity)
    folder = Folder()

    total = 0
    waived = 0
    for path in files():
        rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
        found = scan(path, terms, allow, folder)

        documented = DOCUMENTED.get(rel, frozenset())
        if documented:
            for number in list(found):
                waived += len(found[number] & documented)
                found[number] -= documented
                if not found[number]:
                    del found[number]

        if not found:
            continue
        total += len(found)
        print(rel)
        for number in sorted(found):
            masked = ", ".join(sorted(t[0] + "*" * (len(t) - 1) for t in found[number]))
            print("  %s:%d  %s" % (rel, number, masked))

    if waived:
        print("%d documented mention%s allowed, see DOCUMENTED in this file"
              % (waived, "" if waived == 1 else "s"))

    if total == 0:
        print("no undocumented term of severity %d or above in the published files"
              % args.min_severity)
        return 0

    print("\n%d lines hold a readable term of severity %d or above."
          % (total, args.min_severity))
    return 1 if args.check else 0


if __name__ == "__main__":
    sys.exit(main())
