/// Reads base64 pack text the same way both ports read it.
///
/// The two runtimes disagree, and they disagree in opposite directions:
///
/// | input                  | Dart `base64.decode` | .NET `FromBase64String` |
/// | ---------------------- | -------------------- | ----------------------- |
/// | line-wrapped at 76     | throws               | accepts                 |
/// | spaces or tabs         | throws               | accepts                 |
/// | URL-safe `-` and `_`   | accepts              | throws                  |
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

/// Drops blank characters and folds the URL-safe alphabet to the standard one.
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
    if (c == '-') {
      built.write('+');
    } else if (c == '_') {
      built.write('/');
    } else {
      built.write(c);
    }
  }
  return built.toString();
}
