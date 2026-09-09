#!/usr/bin/env python3
"""Convert a seed or preset JSON document into a .vpk pack.

Use this to host your own term list without putting the words in plain text on
a server or in a CDN cache. Both ports accept a pack anywhere they accept a
seed document, so the result is a drop-in replacement:

    python3 tool/pack.py my-list.json -o my-list.vpk
    python3 tool/pack.py my-list.json --base64 > my-list.txt

Serve the .vpk as bytes, or the base64 text as a string. AddSeed and addSeed
read the format from the file itself, so no caller has to say which it is.

Pass --show to read a pack back and print it as JSON.
"""

import argparse
import base64
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import packlib


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("source", help="the JSON document to pack, or the pack to show")
    parser.add_argument("-o", "--out", help="write the pack here (default: stdout as base64)")
    parser.add_argument("--base64", action="store_true", help="write base64 text, not bytes")
    parser.add_argument("--show", action="store_true", help="read a pack back and print JSON")
    args = parser.parse_args()

    if args.show:
        try:
            doc = packlib.decode(open(args.source, "rb").read())
        except ValueError as error:
            raise SystemExit("%s is not a readable pack: %s" % (args.source, error))
        print(json.dumps(doc, indent=2, ensure_ascii=False))
        return 0

    doc = json.load(open(args.source, encoding="utf-8"))
    if "entries" not in doc:
        raise SystemExit("a seed document must hold an 'entries' array")
    if "profile" not in doc:
        raise SystemExit("a seed document must state its fold 'profile'")

    try:
        pack = packlib.encode(doc)
        # A pack that will not read back is a pack that must not ship.
        packlib.decode(pack)
    except ValueError as error:
        raise SystemExit("cannot pack %s: %s" % (args.source, error))

    if args.out and not args.base64:
        with open(args.out, "wb") as fh:
            fh.write(pack)
        print("wrote %s: %d entries, %d bytes" % (args.out, len(doc["entries"]), len(pack)),
              file=sys.stderr)
        return 0

    text = base64.b64encode(pack).decode("ascii")
    if args.out:
        with open(args.out, "w", encoding="utf-8") as fh:
            fh.write(text)
        print("wrote %s: %d entries, %d base64 characters"
              % (args.out, len(doc["entries"]), len(text)), file=sys.stderr)
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
