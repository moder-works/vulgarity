import 'vulgarity_category.dart';
import 'vulgarity_term.dart';

/// One term found in the text.
///
/// [start] and [end] index the ORIGINAL text the caller passed in, not the
/// folded form. A caller can highlight the real span with them.
///
/// Two different strings are easy to confuse here, so the class keeps them
/// apart. [excerpt] returns the text as it was written — `d.a.m.n`, `H3LL`,
/// `daaamn`. `term.text` returns the list entry the matcher reached, folded to
/// profile fold-v1 — `damn`, `hell`, `damn`. Show the excerpt to a person;
/// group and count by the term.
///
/// ```dart
/// for (final match in filter.scan(text)) {
///   print('${match.excerpt(text)} is ${match.term.text}');
/// }
/// ```
class VulgarityMatch implements Comparable<VulgarityMatch> {
  /// Records one hit. The filter builds these; callers read them.
  const VulgarityMatch(this.start, this.end, this.term, this.termIndex);

  /// Where the match starts in the original text.
  final int start;

  /// Where the match ends in the original text. Exclusive.
  final int end;

  /// The term that matched, with its category, severity and match rule.
  final VulgarityTerm term;

  /// The position of the term in the filter's term list.
  final int termIndex;

  /// How many characters of the original text the match covers.
  int get length => end - start;

  /// The category of the term that matched.
  VulgarityCategory get category => term.category;

  /// The category as the term list spells it.
  ///
  /// This keeps a name the base list does not define, where [category] reports
  /// [VulgarityCategory.other].
  String get categoryName => term.categoryName;

  /// How bad the term that matched is, from 1 to 5.
  int get severity => term.severity;

  /// Returns the exact text the match covers.
  ///
  /// Pass the same string you passed to the filter. The offsets index that
  /// string, so any other one gives nonsense or throws.
  String excerpt(String original) => original.substring(start, end);

  /// Orders matches by start, then by the longer match, then by term.
  @override
  int compareTo(VulgarityMatch other) {
    if (start != other.start) {
      return start < other.start ? -1 : 1;
    }
    if (end != other.end) {
      return end > other.end ? -1 : 1;
    }
    return termIndex.compareTo(other.termIndex);
  }

  @override
  String toString() => '${term.text}@$start..$end';
}
