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

  /// Scan a second time with repeated letters collapsed, so "fuuuck" matches
  /// "fuck".
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
