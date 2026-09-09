import 'vulgarity_category.dart';
import 'vulgarity_term.dart';

/// One term found in the text.
///
/// [start] and [end] index the ORIGINAL text the caller passed in, not the
/// folded form. A caller can highlight the real span with them.
class VulgarityMatch implements Comparable<VulgarityMatch> {
  const VulgarityMatch(this.start, this.end, this.term, this.termIndex);

  /// Where the match starts in the original text.
  final int start;

  /// Where the match ends in the original text. Exclusive.
  final int end;

  /// The term that matched.
  final VulgarityTerm term;

  /// The position of the term in the filter's term list.
  final int termIndex;

  /// How many characters of the original text the match covers.
  int get length => end - start;

  /// The matched term, folded to profile fold-v1.
  String get text => term.text;

  VulgarityCategory get category => term.category;

  String get categoryName => term.categoryName;

  int get severity => term.severity;

  /// Returns the exact text the match covers.
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
