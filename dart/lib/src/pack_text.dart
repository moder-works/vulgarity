/// Reads base64 pack text the same way both ports read it.
///
/// The two runtimes disagree, and they disagree in opposite directions:
///
/// | input                  | Dart `base64.decode` | .NET `FromBase64String` |
/// | ---------------------- | -------------------- | ----------------------- |
/// | line-wrapped at 76     | throws               | accepts                 |
/// | spaces or tabs         | throws               | accepts                 |
/// | URL-safe `-` and `_`   | accepts              | throws                  |
/// | padding left off       | throws               | throws                  |
/// | a leading BOM          | throws               | throws                  |
///
/// So `base64 < pack.vpk`, which wraps at 76 columns, would load on .NET and
/// fail here. This normalises first, so a caller gets one answer on both.
/// `dotnet/src/Vulgarity/PackText.cs` is the mirror of this file.
library;

import 'dart:convert';

import 'pack_reader.dart';

/// Decodes base64 pack text. Returns null when the text is not a pack.
List<int>? tryReadPackText(String text) {
  final String cleaned = normalizePackText(text);
  if (cleaned.isEmpty) {
    return null;
  }

  List<int> bytes;
  try {
    bytes = base64.decode(cleaned);
  } on FormatException {
    return null;
  }

  return looksLikePack(bytes) ? bytes : null;
}

/// Puts base64 pack text into the one shape both decoders accept.
///
/// It drops blank characters, drops a leading byte-order mark, folds the
/// URL-safe alphabet to the standard one, and puts back any `=` padding the
/// sender left off. base64url is normally served unpadded and both decoders
/// demand a multiple of four, so without the last step a URL-safe pack loads
/// only when its length happens to divide.
///
/// A byte-order mark anywhere but the front is left alone: that is corruption,
/// not an encoding choice, and the decode must fail on it.
String normalizePackText(String text) {
  final StringBuffer built = StringBuffer();
  for (int i = 0; i < text.length; i++) {
    final String c = text[i];
    if (c == ' ' ||
        c == '\t' ||
        c == '\r' ||
        c == '\n' ||
        c == '\v' ||
        c == '\f') {
      continue;
    }
    if (c == '\u{FEFF}' && built.isEmpty) {
      continue;
    }
    if (c == '-') {
      built.write('+');
    } else if (c == '_') {
      built.write('/');
    } else {
      built.write(c);
    }
  }

  final String cleaned = built.toString();
  switch (cleaned.length % 4) {
    case 2:
      return '$cleaned==';
    case 3:
      return '$cleaned=';
    default:
      // A remainder of 1 is no base64 at all. Hand it on and let the decoder
      // say so, rather than padding it into something that looks valid.
      return cleaned;
  }
}
