import 'dart:convert';

import 'model/vulgarity_term.dart';
import 'normalization/fold_table.g.dart';
import 'vulgarity_options.dart';

/// The preset schema this build reads.
const int kSupportedPresetSchema = 1;

/// A whole filter policy in one JSON document: the options, the extra terms,
/// the allowlist, and the terms to drop.
///
/// A seed document carries terms alone. A preset carries the policy too, so a
/// server can change how strict a client is without an app release.
///
/// Treat a preset from the network as untrusted. [VulgarityPreset.parse]
/// validates every field and throws [FormatException] with the offending field
/// named. It never partly applies a bad document.
///
/// ```dart
/// final response = await http.get(Uri.parse('https://example.com/policy.json'));
/// final filter = VulgarityFilter.fromPreset(response.body);
/// ```
class VulgarityPreset {
  const VulgarityPreset({
    this.name,
    this.description,
    this.languages = const <String>[],
    required this.options,
    this.entries = const <VulgarityTerm>[],
    this.allow = const <String>[],
    this.remove = const <String>[],
  });

  /// A short name for the policy.
  final String? name;

  /// What the policy is for.
  final String? description;

  /// The bundled term lists to load, by language code.
  final List<String> languages;

  /// The policy. A missing field in the document keeps its default.
  final VulgarityOptions options;

  /// Extra terms the preset adds.
  final List<VulgarityTerm> entries;

  /// Innocent words the filter must never flag.
  final List<String> allow;

  /// Terms to drop after the language packs load.
  final List<String> remove;

  /// Reads a preset document.
  ///
  /// Throws [FormatException] when the document is malformed, when it targets
  /// another fold profile, or when a field is out of range.
  factory VulgarityPreset.parse(String json) {
    final Object? parsed;
    try {
      parsed = jsonDecode(json);
    } on FormatException catch (error) {
      throw FormatException('The preset is not valid JSON. ${error.message}');
    }

    if (parsed is! Map<String, dynamic>) {
      throw const FormatException('A preset must be a JSON object.');
    }

    final Object? schema = parsed['schema'];
    if (schema != null && schema != kSupportedPresetSchema) {
      throw FormatException(
          'This build reads preset schema $kSupportedPresetSchema. '
          'The document states schema $schema.');
    }

    // The profile pins the fold table. A preset built against an older table
    // would match differently, so it fails here.
    final Object? profile = parsed['profile'];
    if (profile != null && profile != kFoldProfile) {
      throw FormatException(
          "This build implements fold profile '$kFoldProfile'. "
          "The preset states '$profile'.");
    }

    final List<VulgarityTerm> entries = <VulgarityTerm>[];
    final Object? rawEntries = parsed['entries'];
    if (rawEntries != null) {
      if (rawEntries is! List) {
        throw const FormatException("'entries' must be an array.");
      }
      for (final Object? entry in rawEntries) {
        entries.add(_readEntry(entry));
      }
    }

    final Object? rawOptions = parsed['options'];
    if (rawOptions != null && rawOptions is! Map<String, dynamic>) {
      throw const FormatException("'options' must be a JSON object.");
    }

    return VulgarityPreset(
      name: parsed['name'] as String?,
      description: parsed['description'] as String?,
      languages: _readStrings(parsed, 'languages'),
      options: rawOptions == null
          ? VulgarityOptions()
          : VulgarityOptions.fromJson(rawOptions as Map<String, dynamic>),
      entries: entries,
      allow: _readStrings(parsed, 'allow'),
      remove: _readStrings(parsed, 'remove'),
    );
  }

  /// Writes this preset as a JSON document.
  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'schema': kSupportedPresetSchema,
      'profile': kFoldProfile,
      if (name != null) 'name': name,
      if (description != null) 'description': description,
      'languages': languages,
      'options': options.toJson(),
      'entries': entries
          .map((VulgarityTerm t) => <String, dynamic>{
                't': t.text,
                'cat': t.categoryName,
                'sev': t.severity,
                if (t.requireBoundary) 'w': true,
              })
          .toList(),
      'allow': allow,
      'remove': remove,
    };
  }

  static List<String> _readStrings(Map<String, dynamic> root, String name) {
    final Object? value = root[name];
    if (value == null) {
      return const <String>[];
    }
    if (value is! List) {
      throw FormatException("'$name' must be an array of strings.");
    }
    final List<String> result = <String>[];
    for (final Object? item in value) {
      if (item is! String) {
        throw FormatException("'$name' must hold strings only.");
      }
      if (item.isNotEmpty) {
        result.add(item);
      }
    }
    return result;
  }

  static VulgarityTerm _readEntry(Object? entry) {
    if (entry is! Map<String, dynamic>) {
      throw const FormatException(
          "Every entry in 'entries' must be a JSON object.");
    }

    final Object? term = entry['t'];
    if (term is! String || term.isEmpty) {
      throw const FormatException(
          "Every entry in 'entries' must hold a non-empty 't' term.");
    }

    final Object? severity = entry['sev'];
    final int sev = severity is int ? severity : 3;
    if (sev < 1 || sev > 5) {
      throw FormatException(
          "Severity must be 1 to 5. Term '$term' states $sev.");
    }

    // A term category the build does not know maps to other. That keeps an
    // older client working when a server adds a category.
    final Object? category = entry['cat'];
    return VulgarityTerm(
      term,
      category is String ? category : 'other',
      sev,
      entry['w'] == true,
    );
  }

  @override
  String toString() => 'preset ${name ?? '(unnamed)'}: '
      '${languages.length} languages, ${entries.length} added, '
      '${remove.length} removed';
}
