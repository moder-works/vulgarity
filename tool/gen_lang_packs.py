#!/usr/bin/env python3
"""Build the opt-in language packs in data/seed.<lang>.json.

Source: https://github.com/master-wayne7/safe_text (MIT, branch develop),
files lib/data/<lang>.dart. That corpus is community-sourced and nobody has
vetted it term by term. So every pack carries "vetted": false, and no pack
loads unless the caller asks for it.

The importer folds each term to profile fold-v1 and drops the noise. It then
drops a term only when a kept term can still find it. A term of
BOUNDARY_MAX_LENGTH characters or fewer requires a word boundary, so it can
never match inside a longer word, and it never justifies dropping one. The
importer proves this before it writes a pack.

Run:
  python3 tool/gen_lang_packs.py --source /path/to/safe_text
  python3 tool/gen_lang_packs.py            # clones the source itself
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from foldlib import Folder  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE_URL = "https://github.com/master-wayne7/safe_text"
SOURCE_REF = "develop"

# The 15 shipped languages. English is hand-curated in data/seed.json, so the
# importer skips it.
LANGUAGES = ["pl", "ru", "fr", "ko", "it", "th", "es", "zh", "ar", "nl", "vi", "hi", "de", "fa"]

LANGUAGE_NAMES = {
    "pl": "Polish", "ru": "Russian", "fr": "French", "ko": "Korean",
    "it": "Italian", "th": "Thai", "es": "Spanish", "zh": "Chinese",
    "ar": "Arabic", "nl": "Dutch", "vi": "Vietnamese", "hi": "Hindi",
    "de": "German", "fa": "Persian",
}

MIN_LENGTH = 4
BOUNDARY_MAX_LENGTH = 6
DEFAULT_CATEGORY = "profanity"
DEFAULT_SEVERITY = 3

TERM_RE = re.compile(r"^\s*'((?:[^'\\]|\\.)*)',\s*$", re.M)


def read_source_terms(path):
    text = open(path, encoding="utf-8").read()
    out = []
    for raw in TERM_RE.findall(text):
        out.append(raw.replace("\\$", "$").replace("\\'", "'").replace("\\\\", "\\"))
    return out


def load_english_words():
    """Common English words, used to keep a pack from firing on English text."""
    path = "/usr/share/dict/words"
    if not os.path.exists(path):
        return set()
    return {w.strip().lower() for w in open(path, encoding="utf-8")
            if w.strip().isalpha() and len(w.strip()) >= 3}


def build_pack(lang, source_dir, folder, english):
    path = os.path.join(source_dir, "lib", "data", lang + ".dart")
    if not os.path.exists(path):
        raise SystemExit("missing source file: " + path)

    raw = read_source_terms(path)
    stats = {"raw": len(raw)}

    folded = {folder.fold(term) for term in raw}
    folded.discard("")
    stats["folded"] = len(folded)

    long_enough = {t for t in folded if len(t) >= MIN_LENGTH}
    stats["length"] = len(long_enough)

    # A pack often runs beside English text, so drop anything that is an
    # ordinary English word.
    cleaned = {t for t in long_enough if t not in english}
    stats["not_english"] = len(cleaned)

    # Drop a term only when a kept term already finds it.
    #
    # A kept term finds a longer term ONLY when it matches inside a word, and a
    # term matches inside a word only when it carries no word-boundary flag.
    # So a short term never justifies dropping a longer one: "jode" requires a
    # boundary, so it can never match inside "joder", and dropping "joder"
    # would lose it completely.
    minimal = []
    for term in sorted(cleaned, key=lambda x: (len(x), x)):
        if not any(kept in term for kept in minimal
                   if len(kept) > BOUNDARY_MAX_LENGTH):
            minimal.append(term)
    minimal.sort()
    stats["minimal"] = len(minimal)

    # Prove the prune was sound: every source term must still be found.
    lost = [t for t in sorted(cleaned)
            if t not in minimal
            and not any(k in t for k in minimal if len(k) > BOUNDARY_MAX_LENGTH)]
    if lost:
        raise SystemExit(
            "prune dropped %d terms nothing can find, for example %s"
            % (len(lost), lost[:5]))

    entries = []
    for term in minimal:
        entry = {"t": term, "cat": DEFAULT_CATEGORY, "sev": DEFAULT_SEVERITY}
        if len(term) <= BOUNDARY_MAX_LENGTH:
            entry["w"] = True
        entries.append(entry)

    doc = {
        "schema": 1,
        "profile": "fold-v1",
        "lang": lang,
        "name": LANGUAGE_NAMES.get(lang, lang),
        "vetted": False,
        "source": SOURCE_URL,
        "license": "MIT",
        "note": (
            "Community-sourced list for " + LANGUAGE_NAMES.get(lang, lang) + ". "
            "Nobody vetted these terms one by one, and the source carries no "
            "category or severity, so every entry takes category '" +
            DEFAULT_CATEGORY + "' and severity " + str(DEFAULT_SEVERITY) + ". "
            "A term of " + str(BOUNDARY_MAX_LENGTH) + " characters or fewer "
            "requires a word boundary. Expect false positives. Load this pack "
            "only when you handle text in this language."
        ),
        "entries": entries,
        "allow": [],
    }

    out_path = os.path.join(ROOT, "data", "seed." + lang + ".json")
    with open(out_path, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, indent=2)
        fh.write("\n")

    return stats


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", help="a safe_text checkout")
    args = parser.parse_args()

    temp_dir = None
    source_dir = args.source
    if not source_dir:
        temp_dir = tempfile.mkdtemp(prefix="safe_text_")
        source_dir = os.path.join(temp_dir, "safe_text")
        print("cloning " + SOURCE_URL)
        subprocess.check_call(
            ["git", "clone", "-q", "--depth", "1", "--branch", SOURCE_REF, SOURCE_URL + ".git", source_dir])

    try:
        folder = Folder()
        english = load_english_words()
        if not english:
            print("WARNING: no English word list found, so the English guard is off.")

        print("%-5s %7s %8s %8s %11s %8s" % ("lang", "raw", "folded", "len>=4", "notEnglish", "shipped"))
        total = 0
        for lang in LANGUAGES:
            s = build_pack(lang, source_dir, folder, english)
            total += s["minimal"]
            print("%-5s %7d %8d %8d %11d %8d"
                  % (lang, s["raw"], s["folded"], s["length"], s["not_english"], s["minimal"]))
        print("%-5s %7s %8s %8s %11s %8d" % ("TOTAL", "", "", "", "", total))
        print("wrote %d packs to data/" % len(LANGUAGES))
    finally:
        if temp_dir:
            shutil.rmtree(temp_dir, ignore_errors=True)

    return 0


if __name__ == "__main__":
    sys.exit(main())
