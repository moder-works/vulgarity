#!/usr/bin/env python3
"""Compile the seed documents into masked packs.

  data/seed.json        -> data/packs/seed-en.vpk
  data/seed.<lang>.json -> data/packs/seed-<lang>.vpk

Both ports read the packs, never the JSON. data/*.json stays the authored,
reviewable source. See tool/packlib.py for the format and for why it exists.

The file names use a hyphen, not a dot. MSBuild reads a `.<code>.` segment in a
file name as a culture, so "seed.de.vpk" would move into a German satellite
assembly. "seed-de.vpk" does not.

Run:   python3 tool/gen_packs.py
Check: python3 tool/gen_packs.py --check
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import packlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "data")
PACKS = os.path.join(DATA, "packs")

LANGUAGES = ["ar", "de", "es", "fa", "fr", "hi", "it", "ko", "nl", "pl", "ru", "th", "vi", "zh"]


def source_path(code):
    """Return the JSON seed that backs one language code."""
    if code == "en":
        return os.path.join(DATA, "seed.json")
    return os.path.join(DATA, "seed." + code + ".json")


def pack_name(code):
    return "seed-" + code + ".vpk"


def build():
    """Return {absolute path: pack bytes} and a per-pack entry count."""
    files = {}
    counts = []
    for code in ["en"] + LANGUAGES:
        doc = json.load(open(source_path(code), encoding="utf-8"))
        pack = packlib.encode(doc)

        # A pack that will not read back is a pack that must not ship.
        back = packlib.decode(pack)
        if len(back["entries"]) != len(doc["entries"]):
            raise SystemExit("pack for '%s' does not read back" % code)

        files[os.path.join(PACKS, pack_name(code))] = pack
        counts.append((code, len(doc["entries"]), len(pack)))
    return files, counts


def main():
    check = "--check" in sys.argv
    files, counts = build()

    stale = []
    for path, content in files.items():
        if check:
            current = open(path, "rb").read() if os.path.exists(path) else None
            if current != content:
                stale.append(os.path.relpath(path, ROOT))
        else:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "wb") as fh:
                fh.write(content)

    if check:
        if stale:
            print("STALE: " + ", ".join(stale))
            return 1
        print("packs are current")
        return 0

    terms = sum(c[1] for c in counts)
    size = sum(c[2] for c in counts)
    print("%d packs, %d terms, %d bytes, written to data/packs/" % (len(counts), terms, size))
    return 0


if __name__ == "__main__":
    sys.exit(main())
