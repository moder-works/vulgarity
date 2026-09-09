import 'dart:convert';

import 'json_read.dart';
import 'model/vulgarity_term.dart';
import 'normalization/fold_table.g.dart';
import 'vulgarity_options.dart';

/// The preset schema this build reads.
const int kSupportedPresetSchema = 1;

/// The severity a preset entry takes when it states none.
///
/// A preset entry is added one at a time and by hand, so an entry that says
/// nothing sits in the middle of the range. A seed entry defaults to 1
/// instead. Both ports use these two numbers.
const int kPresetDefaultSeverity = 3;

/// The shape of a language code: 2 to 8 lower-case letters.
///
/// A preset only has to be well-formed here. Whether this build can serve the
/// code is a question for the builder, which may have a resolver that carries
/// packs this package never heard of.
final RegExp _languageCode = RegExp(r'^[a-z]{2,8}$');

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

  /// The term lists to load, by language code.
  ///
  /// [VulgarityPreset.parse] checks the shape of each code and nothing more.
  /// Whether a code can be served is settled later, by
  /// [VulgarityFilterBuilder.addPreset] and the resolver it was given.
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
  /// another fold profile, or when a field is out of range. Every field is
  /// type-checked rather than coerced, so `"sev": "3"` and `"name": 5` are
  /// both errors.
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

    final int? schema = readOptionalInt(parsed, 'schema');
    if (schema != null && schema != kSupportedPresetSchema) {
      throw FormatException(
          'This build reads preset schema $kSupportedPresetSchema. '
          'The document states schema $schema.');
    }

    // The profile pins the fold table. A preset built against an older table
    // would match differently, so it fails here.
    final String? profile = readOptionalString(parsed, 'profile');
    if (profile != null && profile != kFoldProfile) {
      throw FormatException(
          "This build implements fold profile '$kFoldProfile'. "
          "The preset states '$profile'.");
    }

    final List<VulgarityTerm> entries = <VulgarityTerm>[];
    final Object? rawEntries = parsed['entries'];
    if (rawEntries != null) {
      if (rawEntries is! List) {
        throw FormatException("'entries' must be an array. "
            'The document states ${jsonTypeName(rawEntries)}.');
      }
      for (final Object? entry in rawEntries) {
        entries.add(_readEntry(entry));
      }
    }

    final Object? rawOptions = parsed['options'];
    if (rawOptions != null && rawOptions is! Map<String, dynamic>) {
      throw FormatException("'options' must be a JSON object. "
          'The document states ${jsonTypeName(rawOptions)}.');
    }

    return VulgarityPreset(
      name: readOptionalString(parsed, 'name'),
      description: readOptionalString(parsed, 'description'),
      languages: _readLanguages(parsed),
      options: rawOptions == null
          ? VulgarityOptions()
          : VulgarityOptions.fromJson(rawOptions as Map<String, dynamic>),
      entries: entries,
      allow: readStrings(parsed, 'allow'),
      remove: readStrings(parsed, 'remove'),
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

  /// Reads and shape-checks the language codes.
  ///
  /// This checks the SHAPE only. It deliberately does not ask whether this
  /// build carries the code, because a caller may pass a resolver that serves
  /// codes no bundled list covers.
  static List<String> _readLanguages(Map<String, dynamic> root) {
    final List<String> codes = readStrings(root, 'languages');
    for (final String code in codes) {
      if (!_languageCode.hasMatch(code)) {
        throw FormatException("'languages' names '$code', which is not a "
            'language code. A code is 2 to 8 lower-case letters.');
      }
    }
    return codes;
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

    // A term category the build does not know maps to other. That keeps an
    // older client working when a server adds a category. A category that is
    // not a string is a different thing: that is a malformed document.
    final String category = readOptionalString(entry, 'cat') ?? 'other';

    final int sev = readOptionalInt(entry, 'sev') ?? kPresetDefaultSeverity;
    if (sev < 1 || sev > 5) {
      throw FormatException(
          "Severity must be 1 to 5. Term '$term' states $sev.");
    }

    return VulgarityTerm(
      term,
      category,
      sev,
      readOptionalBool(entry, 'w') ?? false,
    );
  }

  @override
  String toString() => 'preset ${name ?? '(unnamed)'}: '
      '${languages.length} languages, ${entries.length} added, '
      '${remove.length} removed';
}
