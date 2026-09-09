#!/usr/bin/env python3
"""The .vpk pack format: a compact, masked container for a seed document.

Why this exists. The seed JSON used to ship verbatim inside both compiled
artifacts, so `strings` on the assembly printed the whole term list. A pack
carries the same data in a form that holds no readable text.

The format has two jobs, and each layer does one of them:

  1. The compact record layout removes the JSON framing. It cuts the 15 bundled
     lists from 1,281,491 bytes to 194,198.
  2. The RC4 mask removes readable text. The record layout alone does not do
     this, because the term bytes still sit next to each other.

This is obfuscation, not encryption. The key sits beside the data in every
port, so anyone who wants the list can still get it. The goal is only that
nobody reads it by accident.

RC4 is chosen for portability, not for strength. Every value it touches is one
byte, and every operation is `+` and `& 0xFF`. Dart compiles `int` to a
JavaScript double on the web, so a generator that multiplies loses precision
above 2^53, and one that shifts hits JavaScript's ToInt32 rule at 2^31. Byte
arithmetic has no such edge, so Python, C#, dart2js, dart2wasm and the Dart VM
all agree with no special handling.

Layout:

    offset  bytes  field
    0       4      magic "VPK1"                    <- never masked
    --- everything below is XOR-masked ---
    4       1      schema
    5       1      flags (bit 0: an allow list follows)
    6       ...    varint len + UTF-8 profile
            ...    varint count, then per category: varint len + UTF-8 name
            ...    varint entry count, then per entry:
                     varint len + UTF-8 term bytes
                     1 byte: category (4 bits) | boundary (1 bit) | severity (3 bits)
            ...    varint count, then per allow word: varint len + UTF-8 bytes
"""

MAGIC = b"VPK1"

# The mask key. Change this and every pack must be regenerated.
KEY = b"vulgarity-pack-v1"

SCHEMA = 1

# The category index takes 4 bits, so a pack holds at most 16 category names.
MAX_CATEGORIES = 16

FLAG_HAS_ALLOW = 0x01


def keystream(length, key=KEY):
    """Return `length` RC4 bytes for `key`."""
    box = list(range(256))
    j = 0
    for i in range(256):
        j = (j + box[i] + key[i % len(key)]) & 0xFF
        box[i], box[j] = box[j], box[i]

    out = bytearray()
    i = j = 0
    for _ in range(length):
        i = (i + 1) & 0xFF
        j = (j + box[i]) & 0xFF
        box[i], box[j] = box[j], box[i]
        out.append(box[(box[i] + box[j]) & 0xFF])
    return bytes(out)


def mask(body, key=KEY):
    """XOR `body` with the keystream. Masking twice returns the original."""
    stream = keystream(len(body), key)
    return bytes(a ^ b for a, b in zip(body, stream))


def put_varint(out, value):
    """Append `value` to `out` as an unsigned LEB128 varint."""
    if value < 0:
        raise ValueError("a varint holds no negative value")
    while value >= 0x80:
        out.append((value & 0x7F) | 0x80)
        value >>= 7
    out.append(value)


def get_varint(data, pos):
    """Read a varint at `pos`. Return (value, next position)."""
    value = 0
    shift = 0
    while True:
        if pos >= len(data):
            raise ValueError("the pack ends inside a varint")
        byte = data[pos]
        pos += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80:
            return value, pos
        shift += 7
        if shift > 35:
            raise ValueError("a varint runs too long")


def _put_text(out, text):
    raw = text.encode("utf-8")
    put_varint(out, len(raw))
    out += raw


def _get_text(data, pos):
    length, pos = get_varint(data, pos)
    if pos + length > len(data):
        raise ValueError("the pack ends inside a string")
    return data[pos:pos + length].decode("utf-8"), pos + length


def encode(doc, key=KEY):
    """Turn a parsed seed document into pack bytes."""
    entries = doc["entries"]
    profile = doc["profile"]
    allow = [w for w in doc.get("allow", []) if w]

    categories = sorted({e.get("cat", "other") for e in entries})
    if len(categories) > MAX_CATEGORIES:
        raise ValueError(
            "a pack holds at most %d categories, and this one has %d"
            % (MAX_CATEGORIES, len(categories)))
    index = {name: i for i, name in enumerate(categories)}

    body = bytearray()
    body.append(SCHEMA)
    body.append(FLAG_HAS_ALLOW if allow else 0)
    _put_text(body, profile)

    put_varint(body, len(categories))
    for name in categories:
        _put_text(body, name)

    put_varint(body, len(entries))
    for entry in entries:
        term = entry["t"]
        if not term:
            raise ValueError("every entry needs a term")
        severity = entry.get("sev", 1)
        if not 1 <= severity <= 5:
            raise ValueError(
                "severity must be 1 to 5, and term '%s' states %s" % (term, severity))
        _put_text(body, term)
        body.append(
            (index[entry.get("cat", "other")] << 4)
            | ((1 if entry.get("w") else 0) << 3)
            | severity)

    # The flag says whether an allow block follows. When it does not, write
    # nothing here at all, so a reader that stops after the entries lands
    # exactly on the end of the pack.
    if allow:
        put_varint(body, len(allow))
        for word in allow:
            _put_text(body, word)

    return MAGIC + mask(bytes(body), key)


def decode(pack, key=KEY):
    """Turn pack bytes back into a seed document.

    This mirrors the C# and Dart readers. It gives the tools a way to check a
    pack without a build step, and it acts as a third opinion when the two
    ports are checked against each other.
    """
    if len(pack) < len(MAGIC) or pack[:len(MAGIC)] != MAGIC:
        raise ValueError("this is not a pack: the magic does not match")

    data = mask(pack[len(MAGIC):], key)
    if len(data) < 2:
        raise ValueError("the pack holds no header")

    schema = data[0]
    if schema != SCHEMA:
        raise ValueError("this build reads pack schema %d, and the file states %d"
                         % (SCHEMA, schema))
    flags = data[1]
    pos = 2

    profile, pos = _get_text(data, pos)

    count, pos = get_varint(data, pos)
    categories = []
    for _ in range(count):
        name, pos = _get_text(data, pos)
        categories.append(name)

    count, pos = get_varint(data, pos)
    entries = []
    for _ in range(count):
        term, pos = _get_text(data, pos)
        if pos >= len(data):
            raise ValueError("the pack ends inside an entry")
        packed = data[pos]
        pos += 1
        entry = {"t": term, "cat": categories[packed >> 4], "sev": packed & 0x07}
        if packed & 0x08:
            entry["w"] = True
        entries.append(entry)

    doc = {"schema": schema, "profile": profile, "entries": entries}

    if flags & FLAG_HAS_ALLOW:
        count, pos = get_varint(data, pos)
        allow = []
        for _ in range(count):
            word, pos = _get_text(data, pos)
            allow.append(word)
        doc["allow"] = allow

    # Nothing may follow. This catches a truncated pack and a padded one in the
    # same line, and it is what found the missing-allow-block bug.
    if pos != len(data):
        raise ValueError("the pack holds bytes after its last entry")

    return doc
