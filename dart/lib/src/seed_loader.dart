import 'dart:convert';

import 'json_read.dart';
import 'model/vulgarity_term.dart';

/// The seed schema this build reads.
const int kSupportedSchema = 1;

/// The severity a seed entry takes when it states none.
///
/// A seed list is bulk-authored and mostly mild, so an entry that says nothing
/// is treated as mild. A preset entry defaults to 3 instead, because a preset
/// is written one term at a time and its terms are the ones somebody cared
/// enough to add. Both ports use these two numbers.
const int kSeedDefaultSeverity = 1;

/// Reads a seed document into terms and allowlist words.
///
/// Throws [FormatException] on any malformed field. Nothing is coerced: a
/// `sev` that is not a whole number, a `cat` that is not a string and a `w`
/// that is not a boolean are all errors, not defaults.
void loadSeed(
  String json,
  String expectedProfile,
  List<VulgarityTerm> terms,
  List<String> allow,
) {
  final Object? parsed = jsonDecode(json);
  if (parsed is! Map<String, dynamic>) {
    throw const FormatException('A seed document must be a JSON object.');
  }

  final int? schema = readOptionalInt(parsed, 'schema');
  if (schema != null && schema != kSupportedSchema) {
    throw FormatException('This build reads seed schema $kSupportedSchema. '
        'The file states schema $schema.');
  }

  // The profile pins the fold table the seed was built with. A stale file then
  // fails here instead of matching silently wrong.
  final String? profile = readOptionalString(parsed, 'profile');
  if (profile != null && profile != expectedProfile) {
    throw FormatException(
        "This build implements fold profile '$expectedProfile'. "
        "The seed file states '$profile'.");
  }

  final Object? entries = parsed['entries'];
  if (entries is! List) {
    throw const FormatException(
        "A seed document must hold an 'entries' array.");
  }

  for (final Object? entry in entries) {
    terms.add(_readEntry(entry));
  }

  allow.addAll(readStrings(parsed, 'allow'));
}

VulgarityTerm _readEntry(Object? entry) {
  if (entry is! Map<String, dynamic>) {
    throw const FormatException('Every seed entry must be a JSON object.');
  }

  final Object? term = entry['t'];
  if (term is! String || term.isEmpty) {
    throw const FormatException(
        "Every seed entry must hold a non-empty 't' term.");
  }

  final String category = readOptionalString(entry, 'cat') ?? 'other';
  final int sev = readOptionalInt(entry, 'sev') ?? kSeedDefaultSeverity;
  if (sev < 1 || sev > 5) {
    throw FormatException("Severity must be 1 to 5. Term '$term' states $sev.");
  }

  return VulgarityTerm(
    term,
    category,
    sev,
    readOptionalBool(entry, 'w') ?? false,
  );
}
