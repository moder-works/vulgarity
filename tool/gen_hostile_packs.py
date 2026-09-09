#!/usr/bin/env python3
"""Build the hostile pack fixtures that all three readers are tested against.

  -> data/testdata/hostile-packs.json

Each case holds one pack as base64, and what a reader must do with it. The Dart
test, the C# test and tool/test_packlib.py all read this one file, so a pack
that one reader accepts and another refuses fails in exactly one place.

Every pack here is written by hand rather than through packlib.encode, because
the point of most of them is a body the encoder would refuse to write. The few
that are well-formed go through the same helpers the encoder uses, so a valid
case stays valid when the format changes.

Run:   python3 tool/gen_hostile_packs.py
Check: python3 tool/gen_hostile_packs.py --check
"""

import base64
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import packlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "data", "testdata", "hostile-packs.json")

PROFILE = "fold-v1"

# A term byte: category index (4 bits) | boundary (1 bit) | severity (3 bits).
def packed(category=0, boundary=False, severity=3):
    return (category << 4) | ((1 if boundary else 0) << 3) | severity


def text(value):
    out = bytearray()
    packlib._put_text(out, value)
    return bytes(out)


def varint(value):
    out = bytearray()
    packlib.put_varint(out, value)
    return bytes(out)


def body(profile=PROFILE, schema=packlib.SCHEMA, flags=0, categories=None,
         entries=None, allow=None, tail=b""):
    """Assemble a pack body from parts. Every part may be handed raw bytes."""
    categories = ["profanity"] if categories is None else categories
    entries = [("blorp", packed()), ("zorkmid", packed(severity=2))] \
        if entries is None else entries

    out = bytearray()
    out.append(schema)
    out.append(flags)
    out += text(profile)

    if isinstance(categories, bytes):
        out += categories
    else:
        out += varint(len(categories))
        for name in categories:
            out += text(name)

    if isinstance(entries, bytes):
        out += entries
    else:
        out += varint(len(entries))
        for term, flag in entries:
            out += text(term) if isinstance(term, str) else term
            out.append(flag)

    if allow is not None:
        out += allow if isinstance(allow, bytes) else (
            varint(len(allow)) + b"".join(text(w) for w in allow))

    return bytes(out) + tail


def wrap(raw, magic=packlib.MAGIC):
    """Mask a body and put the magic in front, the way encode does."""
    return magic + packlib.mask(raw)


def case(name, pack, expect, terms=None, note=None):
    entry = {"name": name, "pack": base64.b64encode(pack).decode("ascii"),
             "expect": expect}
    if terms is not None:
        entry["terms"] = terms
    if note is not None:
        entry["note"] = note
    return entry


def build():
    cases = []

    # -- the shape every other case departs from --
    good = body()
    cases.append(case("a valid tiny pack", wrap(good), "loads", terms=2))

    cases.append(case(
        "a valid tiny pack with an allow list",
        wrap(body(flags=packlib.FLAG_HAS_ALLOW, allow=["scunthorpe"])),
        "loads", terms=2))

    # -- truncation --
    cases.append(case(
        "truncated at the header",
        packlib.MAGIC + packlib.mask(b"\x01"),
        "error", note="A pack must carry a schema byte and a flags byte."))

    cases.append(case(
        "truncated inside a string",
        wrap(bytes(bytearray([packlib.SCHEMA, 0])) + varint(40) + b"fold-v1"),
        "error", note="The profile claims 40 bytes and the pack holds 7."))

    cases.append(case(
        "truncated inside an entry",
        wrap(body(entries=varint(2) + text("blorp") + bytes([packed()])
                  + text("zorkmid"))),
        "error", note="The second entry has a term but no flag byte."))

    # -- the header --
    cases.append(case("a bad magic", wrap(good, magic=b"VPK9"), "error"))
    cases.append(case("schema 2", wrap(body(schema=2)), "error"))
    cases.append(case("a wrong fold profile", wrap(body(profile="fold-v0")),
                      "error"))

    # -- the entry flag byte --
    cases.append(case(
        "a category index beyond the table",
        wrap(body(entries=[("blorp", packed(category=5))])),
        "error", note="The table holds one name and the entry asks for index 5."))

    cases.append(case(
        "severity 0",
        wrap(body(entries=[("blorp", packed(severity=0))])), "error"))

    cases.append(case(
        "severity 6",
        wrap(body(entries=[("blorp", packed(severity=6))])), "error"))

    # A second, well-formed entry keeps this above the "more entries than it
    # holds" guard, so it is the empty term itself that each reader refuses.
    cases.append(case(
        "an empty term",
        wrap(body(entries=[("", packed()), ("zorkmid", packed(severity=2))])),
        "error"))

    # -- what follows the last entry --
    cases.append(case("trailing bytes", wrap(good + b"\x00\x00"), "error"))

    # -- hostile numbers --
    #
    # Each of these is a varint the encoder refuses to write. They are here to
    # pin what a reader does with one that arrives anyway.
    cases.append(case(
        "a five byte varint as the entry count",
        wrap(body(entries=b"\x85\x80\x80\x80\x10")),
        "error", note="0x10 at the fifth byte asks for more than 31 bits."))

    cases.append(case(
        "a category count of FF FF FF FF 0F",
        wrap(body(categories=b"\xFF\xFF\xFF\xFF\x0F")),
        "error", note="0x0F at the fifth byte asks for more than 31 bits."))

    cases.append(case(
        "an allow count of exactly 2^31-1",
        wrap(body(flags=packlib.FLAG_HAS_ALLOW,
                  allow=b"\xFF\xFF\xFF\xFF\x07")),
        "error",
        note="The count is inside the varint cap, so it is read. The pack then "
             "runs out of bytes long before the count is met, and no reader "
             "allocates for it first."))

    cases.append(case(
        "a six byte varint",
        wrap(body(entries=b"\x80\x80\x80\x80\x80\x01")),
        "error", note="A varint never runs to a sixth byte."))

    cases.append(case(
        "a string length that reads as -1 in int32",
        wrap(body(entries=varint(1) + b"\xFF\xFF\xFF\xFF\x0F")),
        "error",
        note="0xFFFFFFFF is -1 read as a signed int32. The 31 bit cap refuses "
             "it before any reader can turn it into a negative length."))

    # -- a category table one wider than the 4 bit index --
    #
    # packlib.encode refuses to write this; tool/test_packlib.py checks that.
    # The readers have no cap of their own, so a hand-built one still loads:
    # the index is 4 bits, so names 17 and beyond are simply unreachable.
    seventeen = ["cat%02d" % i for i in range(17)]
    cases.append(case(
        "a category table of 17 names",
        wrap(body(categories=seventeen)),
        "loads", terms=2,
        note="The writer refuses this (packlib.MAX_CATEGORIES). No reader caps "
             "the table, and a 4 bit index cannot reach past name 16, so the "
             "pack loads and the last name is dead weight."))

    # -- corruption the format cannot see --
    flipped = bytearray(wrap(good))
    flipped[-4] ^= 0x01
    cases.append(case(
        "one body byte flipped",
        bytes(flipped),
        "loads", terms=2,
        note="The format carries no checksum and no signature, so a flip that "
             "lands inside a term changes the term and nothing complains. This "
             "case records that honestly: it loads, with a term the author "
             "never wrote. Adding an integrity check would turn this into an "
             "error case."))

    return {
        "note": "Hostile .vpk packs. Read by dart/test/hostile_pack_test.dart, "
                "dotnet/tests/Vulgarity.Tests/HostilePackTests.cs and "
                "tool/test_packlib.py. Generated by tool/gen_hostile_packs.py; "
                "do not edit by hand.",
        "profile": PROFILE,
        "cases": cases,
    }


def main():
    check = "--check" in sys.argv
    content = json.dumps(build(), indent=2) + "\n"

    if check:
        current = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else None
        if current != content:
            print("STALE: " + os.path.relpath(OUT, ROOT))
            return 1
        print("hostile packs are current")
        return 0

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        fh.write(content)
    print("%d hostile packs written to %s"
          % (len(build()["cases"]), os.path.relpath(OUT, ROOT)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
