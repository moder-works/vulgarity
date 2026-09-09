/// Field readers shared by the seed, preset and options parsers.
///
/// A document from the network is untrusted, so no reader here ever coerces.
/// `"minSeverity": "3"` is a document the sender got wrong, and saying so is
/// more useful than guessing what was meant.
///
/// Two rules hold for every reader:
///
///  * A missing field and an explicit `null` mean the same thing. Both leave
///    the caller's default in place.
///  * A field that is present with the wrong type throws [FormatException],
///    naming the field and the type the schema wants.
///
/// `dotnet/src/Vulgarity/JsonRead.cs` is the mirror of this file.
library;

/// Names the JSON type of [value] for an error message.
String jsonTypeName(Object? value) {
  if (value == null) {
    return 'null';
  }
  if (value is String) {
    return 'a string';
  }
  if (value is bool) {
    return 'a boolean';
  }
  if (value is num) {
    return 'a number';
  }
  if (value is List) {
    return 'an array';
  }
  if (value is Map) {
    return 'an object';
  }
  return 'a $value';
}

/// Reads a whole number. Returns null when the field is absent.
///
/// A fractional number is refused, so `1.0` is not a stand-in for `1`. On
/// dart2js every number is a double and `1.0` is indistinguishable from `1`,
/// which is a limit of the runtime rather than of this check.
int? readOptionalInt(Map<String, dynamic> json, String field) {
  final Object? value = json[field];
  if (value == null) {
    return null;
  }
  if (value is! int) {
    throw FormatException("'$field' must be a whole number. "
        'The document states ${jsonTypeName(value)}.');
  }
  return value;
}

/// Reads a boolean. Returns null when the field is absent.
bool? readOptionalBool(Map<String, dynamic> json, String field) {
  final Object? value = json[field];
  if (value == null) {
    return null;
  }
  if (value is! bool) {
    throw FormatException("'$field' must be true or false. "
        'The document states ${jsonTypeName(value)}.');
  }
  return value;
}

/// Reads a string. Returns null when the field is absent.
String? readOptionalString(Map<String, dynamic> json, String field) {
  final Object? value = json[field];
  if (value == null) {
    return null;
  }
  if (value is! String) {
    throw FormatException("'$field' must be a string. "
        'The document states ${jsonTypeName(value)}.');
  }
  return value;
}

/// Reads an array of strings. A missing field returns an empty list.
///
/// An empty string carries no meaning in any of these lists, so it drops out
/// rather than failing. A value of another type is a malformed document.
List<String> readStrings(Map<String, dynamic> json, String field) {
  final Object? value = json[field];
  if (value == null) {
    return const <String>[];
  }
  if (value is! List) {
    throw FormatException("'$field' must be an array of strings. "
        'The document states ${jsonTypeName(value)}.');
  }

  final List<String> result = <String>[];
  for (final Object? item in value) {
    if (item is! String) {
      throw FormatException("'$field' must hold strings only. "
          'It holds ${jsonTypeName(item)}.');
    }
    if (item.isNotEmpty) {
      result.add(item);
    }
  }
  return result;
}
