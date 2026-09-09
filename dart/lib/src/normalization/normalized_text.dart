/// A folded view of some input text, with a map back to the original offsets.
///
/// Every list has the same length. Index `i` describes one folded character.
class NormalizedText {
  const NormalizedText(
    this.chars,
    this.srcStart,
    this.srcEnd,
    this.hard,
    this.gap, {
    this.runLength,
  });

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

  /// How many characters of the source stream this one stands for.
  ///
  /// Only a squeezed stream carries this; a plain fold leaves it null, where
  /// every character stands for exactly itself. A plain flag would say only
  /// that a run collapsed here, and the matcher needs the size of the run: a
  /// term spelled with a doubled letter may only match text that doubled that
  /// same letter, so "heel" must not stand in for "hell".
  final List<int>? runLength;

  /// How long the run at `i` was before the squeeze. One on a plain fold.
  int runLengthAt(int i) {
    final List<int>? runs = runLength;
    return runs == null ? 1 : runs[i];
  }

  /// True when the character at `i` stands for a run that was collapsed.
  bool collapsedAt(int i) => runLengthAt(i) > 1;

  int get length => chars.length;
}
