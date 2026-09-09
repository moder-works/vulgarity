import 'json_read.dart';
import 'model/vulgarity_category.dart';

/// Controls what the filter reports and how it masks text.
///
/// Options are immutable. Build a set once and share it, or derive one with
/// [copyWith]. A filter takes them at build time, and `withOptions` swaps them
/// without recompiling the trie.
class VulgarityOptions {
  /// Creates a set of options. Every field has a usable default.
  const VulgarityOptions({
    this.minSeverity = 1,
    this.categories,
    this.maskChar = '*',
    this.maskToken,
    this.repeatTolerance = true,
    this.collapseContained = true,
    this.scoreMode = ScoreMode.total,
  });

  /// Ignore any term below this severity. The range is 1 to 5.
  ///
  /// Raise this to 2 to keep clinical anatomy out of the results.
  final int minSeverity;

  /// Match only these categories. Null means match every category.
  final Set<VulgarityCategory>? categories;

  /// The character that [VulgarityFilter.filter] repeats.
  final String maskChar;

  /// A fixed replacement string. It overrides [maskChar].
  final String? maskToken;

  /// Scan a second time with repeated letters collapsed, so "daaamn" matches
  /// "damn".
  final bool repeatTolerance;

  /// Drop a match that sits fully inside a longer match.
  final bool collapseContained;

  /// How [VulgarityFilter.score] combines severities.
  final ScoreMode scoreMode;

  /// Returns a copy with the named fields replaced.
  VulgarityOptions copyWith({
    int? minSeverity,
    Set<VulgarityCategory>? categories,
    String? maskChar,
    String? maskToken,
    bool? repeatTolerance,
    bool? collapseContained,
    ScoreMode? scoreMode,
  }) {
    return VulgarityOptions(
      minSeverity: minSeverity ?? this.minSeverity,
      categories: categories ?? this.categories,
      maskChar: maskChar ?? this.maskChar,
      maskToken: maskToken ?? this.maskToken,
      repeatTolerance: repeatTolerance ?? this.repeatTolerance,
      collapseContained: collapseContained ?? this.collapseContained,
      scoreMode: scoreMode ?? this.scoreMode,
    );
  }

  /// Reads options from the JSON shape a preset uses.
  ///
  /// Every field is optional. A missing field, or one that is explicitly
  /// `null`, keeps its default. A field of the wrong type, or one out of
  /// range, throws [FormatException] naming the field.
  ///
  /// The fields are checked in a fixed order — minSeverity, maskChar,
  /// maskToken, repeatTolerance, collapseContained, scoreMode, categories —
  /// so a document with two bad fields names the same one in both ports.
  factory VulgarityOptions.fromJson(Map<String, dynamic> json) {
    final int minSeverity = readOptionalInt(json, 'minSeverity') ?? 1;
    if (minSeverity < 1 || minSeverity > 5) {
      throw FormatException(
          "'minSeverity' must be 1 to 5. It states $minSeverity.");
    }

    final String maskChar = readOptionalString(json, 'maskChar') ?? '*';
    if (maskChar.length != 1) {
      throw FormatException(
          "'maskChar' must be exactly one character. It states '$maskChar'.");
    }

    final String? maskToken = readOptionalString(json, 'maskToken');
    if (maskToken != null && maskToken.isEmpty) {
      throw const FormatException(
          "'maskToken' must not be empty. Leave it out to mask by character.");
    }

    final bool repeatTolerance =
        readOptionalBool(json, 'repeatTolerance') ?? true;
    final bool collapseContained =
        readOptionalBool(json, 'collapseContained') ?? true;

    final String? mode = readOptionalString(json, 'scoreMode');
    if (mode != null && mode != 'total' && mode != 'max') {
      throw FormatException(
          "'scoreMode' must be 'total' or 'max'. It states '$mode'.");
    }

    final VulgarityOptions options = VulgarityOptions(
      minSeverity: minSeverity,
      categories: _readCategories(json),
      maskChar: maskChar,
      maskToken: maskToken,
      repeatTolerance: repeatTolerance,
      collapseContained: collapseContained,
      scoreMode: mode == 'max' ? ScoreMode.max : ScoreMode.total,
    );

    // The checks above cover every rule validate knows, so this is a net rather
    // than a second opinion: a rule added to validate later must still reach a
    // caller of fromJson as a FormatException, never as a RangeError.
    try {
      options.validate();
    } on ArgumentError catch (error) {
      // RangeError is an ArgumentError, so this catches both. The error names
      // the field it rejected.
      throw FormatException('These options are not usable. $error');
    }
    return options;
  }

  /// Reads the category filter.
  ///
  /// An empty array stays an empty set. The two mean different things — an
  /// empty set matches no category at all, and a missing field matches every
  /// one — so mapping one onto the other would invert the policy on a round
  /// trip through JSON.
  static Set<VulgarityCategory>? _readCategories(Map<String, dynamic> json) {
    final Object? names = json['categories'];
    if (names == null) {
      return null;
    }
    if (names is! List) {
      throw FormatException("'categories' must be an array of names. "
          'The document states ${jsonTypeName(names)}.');
    }

    final Set<VulgarityCategory> parsed = <VulgarityCategory>{};
    for (final Object? name in names) {
      // An unknown name here would silently match nothing, so it fails
      // loudly. A term with an unknown category is different: that one maps
      // to VulgarityCategory.other.
      final VulgarityCategory? category =
          name is String ? VulgarityCategory.tryParse(name) : null;
      if (category == null) {
        throw FormatException(
            "'categories' names '$name', which this build does not know. "
            'Valid names: ${VulgarityCategory.allNames.join(', ')}.');
      }
      parsed.add(category);
    }
    return parsed;
  }

  /// Writes these options in the JSON shape a preset uses.
  ///
  /// A null [categories] leaves the key out altogether, because writing
  /// `"categories": null` and writing `"categories": []` would read back as
  /// opposite policies.
  Map<String, dynamic> toJson() {
    final Set<VulgarityCategory>? selected = categories;
    return <String, dynamic>{
      'minSeverity': minSeverity,
      if (selected != null)
        'categories': VulgarityCategory.allNames
            .where(
                (String n) => selected.contains(VulgarityCategory.tryParse(n)))
            .toList(),
      'maskChar': maskChar,
      'maskToken': maskToken,
      'repeatTolerance': repeatTolerance,
      'collapseContained': collapseContained,
      'scoreMode': scoreMode == ScoreMode.max ? 'max' : 'total',
    };
  }

  /// Throws when a field is out of range.
  ///
  /// A filter calls this for you at build time, so you rarely need it. Call it
  /// yourself to check options you assembled from user input.
  void validate() {
    if (minSeverity < 1 || minSeverity > 5) {
      throw RangeError.range(minSeverity, 1, 5, 'minSeverity');
    }
    if (maskChar.length != 1) {
      throw ArgumentError.value(
          maskChar, 'maskChar', 'maskChar must be exactly one character.');
    }
    if (maskToken != null && maskToken!.isEmpty) {
      throw ArgumentError.value(maskToken, 'maskToken',
          'maskToken must not be empty. Use null to mask by character.');
    }
  }

  @override
  String toString() {
    final Set<VulgarityCategory>? selected = categories;
    return 'VulgarityOptions(minSeverity: $minSeverity, '
        'categories: ${selected == null ? 'all' : selected.map(
              (VulgarityCategory c) => c.name,
            ).join('|')}, '
        'maskChar: $maskChar, maskToken: $maskToken, '
        'repeatTolerance: $repeatTolerance, '
        'collapseContained: $collapseContained, '
        'scoreMode: ${scoreMode.name})';
  }
}

/// How [VulgarityFilter.score] combines severities.
enum ScoreMode {
  /// Add up the severity of every match.
  total,

  /// Take the highest severity of any match.
  max,
}
