/// A folded view of some input text, with a map back to the original offsets.
///
/// Every list has the same length. Index `i` describes one folded character.
class NormalizedText {
  const NormalizedText(
    this.chars,
    this.srcStart,
    this.srcEnd,
    this.hard,
    this.gap,
  );

  /// The folded code points.
  final List<int> chars;

  /// Where [chars] at `i` starts in the original string.
  final List<int> srcStart;

  /// Where [chars] at `i` ends in the original string. Exclusive.
  final List<int> srcEnd;

  /// True when the source character was a real word character.
  ///
  /// A leetspeak stand-in such as `@` or `!` folds to a letter but stays false
  /// here. The matcher then treats it as a word boundary.
  final List<bool> hard;

  /// True when a word-breaking separator was dropped just before this character.
  final List<bool> gap;

  int get length => chars.length;
}
