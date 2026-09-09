import 'dart:convert';
import 'dart:typed_data';

import 'model/vulgarity_term.dart';

/// The pack schema this build reads.
const int kSupportedPackSchema = 1;

/// The magic every pack starts with. It is never masked, so a reader can
/// recognise the format without the key.
const List<int> kPackMagic = <int>[0x56, 0x50, 0x4B, 0x31]; // "VPK1"

const int _flagHasAllow = 0x01;

// The mask key. It must match KEY in tool/packlib.py.
const List<int> _key = <int>[
  0x76, 0x75, 0x6C, 0x67, 0x61, 0x72, 0x69, 0x74, 0x79, // "vulgarity"
  0x2D, 0x70, 0x61, 0x63, 0x6B, // "-pack"
  0x2D, 0x76, 0x31, // "-v1"
];

/// True when these bytes start with the pack magic.
bool looksLikePack(List<int> pack) {
  if (pack.length < kPackMagic.length) {
    return false;
  }
  for (int i = 0; i < kPackMagic.length; i++) {
    if (pack[i] != kPackMagic[i]) {
      return false;
    }
  }
  return true;
}

/// Reads a masked pack document into terms and allowlist words.
///
/// A pack carries the same data as a seed document, in a form that holds no
/// readable text. The bundled term lists ship as packs, so a compiled app no
/// longer carries the whole list as readable strings.
///
/// This is obfuscation, not encryption. The key sits in this file, so anyone
/// who wants the list can still get it. The goal is only that nobody reads it
/// by accident. `tool/packlib.py` holds the format and the encoder.
void loadPack(
  List<int> pack,
  String expectedProfile,
  List<VulgarityTerm> terms,
  List<String> allow,
) {
  if (!looksLikePack(pack)) {
    throw const FormatException(
        'This is not a pack. The magic does not match.');
  }

  final Uint8List data = _unmask(pack, kPackMagic.length);
  if (data.length < 2) {
    throw const FormatException('A pack must hold a header.');
  }

  final int schema = data[0];
  if (schema != kSupportedPackSchema) {
    throw FormatException('This build reads pack schema $kSupportedPackSchema. '
        'The file states schema $schema.');
  }

  final int flags = data[1];
  final _Cursor at = _Cursor(2);

  // The profile pins the fold table the pack was built with. A stale file then
  // fails here instead of matching silently wrong.
  final String profile = _readText(data, at);
  if (profile != expectedProfile) {
    throw FormatException(
        "This build implements fold profile '$expectedProfile'. "
        "The pack states '$profile'.");
  }

  final int categoryCount = _readVarint(data, at);
  final List<String> categories = <String>[
    for (int i = 0; i < categoryCount; i++) _readText(data, at),
  ];

  final int entryCount = _readVarint(data, at);

  // An entry costs at least three bytes: a length, one character, and the
  // flags. Check that before building a list, so a hostile count cannot make
  // this allocate.
  if (entryCount > (data.length - at.pos) ~/ 3) {
    throw const FormatException('The pack claims more entries than it holds.');
  }

  for (int i = 0; i < entryCount; i++) {
    final String term = _readText(data, at);
    if (term.isEmpty) {
      throw const FormatException('A pack term must not be empty.');
    }

    if (at.pos >= data.length) {
      throw const FormatException('The pack ends inside an entry.');
    }

    final int packed = data[at.pos];
    at.pos++;

    final int index = packed >> 4;
    if (index >= categoryCount) {
      throw const FormatException(
          'A pack entry names a category the table does not hold.');
    }

    final int severity = packed & 0x07;
    if (severity < 1 || severity > 5) {
      throw FormatException(
          "Severity must be 1 to 5. Term '$term' states $severity.");
    }

    terms.add(VulgarityTerm(
      term,
      categories[index],
      severity,
      (packed & 0x08) != 0,
    ));
  }

  if (flags & _flagHasAllow != 0) {
    final int allowCount = _readVarint(data, at);
    for (int i = 0; i < allowCount; i++) {
      final String word = _readText(data, at);
      if (word.isNotEmpty) {
        allow.add(word);
      }
    }
  }

  // Nothing may follow. This catches a truncated pack and a padded one in the
  // same line.
  if (at.pos != data.length) {
    throw const FormatException('The pack holds bytes after its last entry.');
  }
}

/// Strips the magic and XORs the rest with the keystream.
///
/// Every value here is one byte, and every step is `+` and `& 0xFF`. That keeps
/// the result identical on the Dart VM, on dart2js and on dart2wasm, where an
/// `int` is a JavaScript double and wider arithmetic would not agree.
Uint8List _unmask(List<int> pack, int offset) {
  final Uint8List box = Uint8List(256);
  for (int i = 0; i < 256; i++) {
    box[i] = i;
  }

  int j = 0;
  for (int i = 0; i < 256; i++) {
    j = (j + box[i] + _key[i % _key.length]) & 0xFF;
    final int swap = box[i];
    box[i] = box[j];
    box[j] = swap;
  }

  final int length = pack.length - offset;
  final Uint8List out = Uint8List(length);
  int a = 0;
  int b = 0;
  for (int i = 0; i < length; i++) {
    a = (a + 1) & 0xFF;
    b = (b + box[a]) & 0xFF;
    final int swap = box[a];
    box[a] = box[b];
    box[b] = swap;
    out[i] = pack[offset + i] ^ box[(box[a] + box[b]) & 0xFF];
  }

  return out;
}

class _Cursor {
  _Cursor(this.pos);

  int pos;
}

int _readVarint(Uint8List data, _Cursor at) {
  int value = 0;
  int shift = 0;
  while (true) {
    if (at.pos >= data.length) {
      throw const FormatException('The pack ends inside a number.');
    }

    final int b = data[at.pos];
    at.pos++;
    value |= (b & 0x7F) << shift;
    if (b < 0x80) {
      return value;
    }

    shift += 7;
    if (shift > 28) {
      throw const FormatException('A pack number runs too long.');
    }
  }
}

String _readText(Uint8List data, _Cursor at) {
  final int length = _readVarint(data, at);
  if (at.pos + length > data.length) {
    throw const FormatException('The pack ends inside a string.');
  }
  final String text = utf8.decode(data.sublist(at.pos, at.pos + length));
  at.pos += length;
  return text;
}
