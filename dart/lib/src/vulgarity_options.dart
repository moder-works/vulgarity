import 'model/vulgarity_category.dart';

/// Controls what the filter reports and how it masks text.
class VulgarityOptions {
  VulgarityOptions({
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
  /// Every field is optional. A missing field keeps its default.
  factory VulgarityOptions.fromJson(Map<String, dynamic> json) {
    Set<VulgarityCategory>? categories;
    final Object? names = json['categories'];
    if (names != null) {
      if (names is! List) {
        throw const FormatException("'categories' must be an array of names.");
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
      categories = parsed.isEmpty ? null : parsed;
    }

    final Object? mode = json['scoreMode'];
    if (mode != null && mode != 'total' && mode != 'max') {
      throw FormatException(
          "'scoreMode' must be 'total' or 'max'. It states '$mode'.");
    }

    final Object? mask = json['maskChar'];
    if (mask != null && (mask is! String || mask.length != 1)) {
      throw const FormatException("'maskChar' must be exactly one character.");
    }

    final VulgarityOptions options = VulgarityOptions(
      minSeverity: json['minSeverity'] as int? ?? 1,
      categories: categories,
      maskChar: mask as String? ?? '*',
      maskToken: json['maskToken'] as String?,
      repeatTolerance: json['repeatTolerance'] as bool? ?? true,
      collapseContained: json['collapseContained'] as bool? ?? true,
      scoreMode: mode == 'max' ? ScoreMode.max : ScoreMode.total,
    );
    options.validate();
    return options;
  }

  /// Writes these options in the JSON shape a preset uses.
  Map<String, dynamic> toJson() {
    final Set<VulgarityCategory>? selected = categories;
    return <String, dynamic>{
      'minSeverity': minSeverity,
      'categories': selected == null
          ? null
          : VulgarityCategory.allNames
              .where((String n) =>
                  selected.contains(VulgarityCategory.tryParse(n)))
              .toList(),
      'maskChar': maskChar,
      'maskToken': maskToken,
      'repeatTolerance': repeatTolerance,
      'collapseContained': collapseContained,
      'scoreMode': scoreMode == ScoreMode.max ? 'max' : 'total',
    };
  }

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
}
