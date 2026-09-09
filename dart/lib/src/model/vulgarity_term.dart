import 'vulgarity_category.dart';

/// One term in the list, together with how the matcher must treat it.
class VulgarityTerm {
  VulgarityTerm(
    this.text,
    this.categoryName,
    this.severity,
    this.requireBoundary,
  ) : category = VulgarityCategory.parse(categoryName);

  /// The term, folded to profile fold-v1.
  final String text;

  /// The category as the seed file spells it.
  final String categoryName;

  /// The category as an enum. A name the base list does not define maps to
  /// [VulgarityCategory.other].
  final VulgarityCategory category;

  /// How bad the term is, from 1 to 5.
  final int severity;

  /// True when a match must sit on a word boundary.
  final bool requireBoundary;

  @override
  String toString() => '$text ($categoryName, $severity)';
}
