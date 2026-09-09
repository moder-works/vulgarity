import 'dart:convert';

import 'model/vulgarity_term.dart';

/// The seed schema this build reads.
const int kSupportedSchema = 1;

/// Reads a seed document into terms and allowlist words.
void loadSeed(
  String json,
  String expectedProfile,
  List<VulgarityTerm> terms,
  List<String> allow,
) {
  final Object? parsed = jsonDecode(json);
  if (parsed is! Map<String, dynamic>) {
    throw FormatException('A seed document must be a JSON object.');
  }

  final Object? schema = parsed['schema'];
  if (schema != null && schema != kSupportedSchema) {
    throw FormatException('This build reads seed schema $kSupportedSchema. '
        'The file states schema $schema.');
  }

  // The profile pins the fold table the seed was built with. A stale file then
  // fails here instead of matching silently wrong.
  final Object? profile = parsed['profile'];
  if (profile != null && profile != expectedProfile) {
    throw FormatException(
        "This build implements fold profile '$expectedProfile'. "
        "The seed file states '$profile'.");
  }

  final Object? entries = parsed['entries'];
  if (entries is! List) {
    throw FormatException("A seed document must hold an 'entries' array.");
  }

  for (final Object? entry in entries) {
    terms.add(_readEntry(entry));
  }

  final Object? allowed = parsed['allow'];
  if (allowed is List) {
    for (final Object? word in allowed) {
      if (word is String && word.isNotEmpty) {
        allow.add(word);
      }
    }
  }
}

VulgarityTerm _readEntry(Object? entry) {
  if (entry is! Map<String, dynamic>) {
    throw FormatException('Every seed entry must be a JSON object.');
  }

  final Object? term = entry['t'];
  if (term is! String || term.isEmpty) {
    throw FormatException("Every seed entry must hold a non-empty 't' term.");
  }

  final Object? category = entry['cat'];
  final Object? severity = entry['sev'];
  final int sev = severity is int ? severity : 1;

  if (sev < 1 || sev > 5) {
    throw FormatException(
        "Severity must be 1 to 5. Term '$term' states $sev.");
  }

  return VulgarityTerm(
    term,
    category is String ? category : 'other',
    sev,
    entry['w'] == true,
  );
}
