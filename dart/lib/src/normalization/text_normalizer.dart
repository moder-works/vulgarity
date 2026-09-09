import 'fold_table.g.dart';
import 'normalized_text.dart';

/// The character was a separator that ends a word. The normalizer drops it.
const int kindDropBreak = 0;

/// The character was an invisible mark. The normalizer drops it, and it does
/// not end a word.
const int kindDropSilent = 1;

/// The character stands in for a letter, as `@` does for `a`. It matches like a
/// letter, but it does not count as a word character.
const int kindSoft = 2;

/// The character is a real word character.
const int kindHard = 3;

/// The lowest code point that any algorithmic range covers.
final int _minRangeStart = kFoldRanges
    .map((List<int> r) => r[0])
    .reduce((int a, int b) => a < b ? a : b);

/// Folds text to profile fold-v1 and keeps a map back to the original offsets.
abstract final class TextNormalizer {
  /// Classifies one code point and reports what it folds to.
  ///
  /// Returns the kind, the single folded code point, and the folded string when
  /// the fold produces more than one character. When `multi` is not null, the
  /// caller must use it and ignore `single`.
  static (int kind, int single, String? multi) classify(int cp) {
    // The common case first. Neither fold map holds an ASCII letter.
    if (cp >= 0x61 && cp <= 0x7A) {
      return (kindHard, cp, null);
    }
    if (cp >= 0x41 && cp <= 0x5A) {
      return (kindHard, cp + 32, null);
    }

    final String? soft = kFoldSoft[cp];
    if (soft != null) {
      return _split(soft, kindSoft);
    }

    final String? hard = kFoldHard[cp];
    if (hard != null) {
      return _split(hard, kindHard);
    }

    // Digits that carry no leetspeak meaning stay as themselves.
    if (cp >= 0x30 && cp <= 0x39) {
      return (kindHard, cp, null);
    }

    if (cp >= _minRangeStart) {
      for (final List<int> r in kFoldRanges) {
        if (cp >= r[0] && cp <= r[1]) {
          return (kindHard, r[2] + (cp - r[0]), null);
        }
      }
    }

    if (_inRanges(kDropBreak, cp)) {
      return (kindDropBreak, -1, null);
    }
    if (_inRanges(kDropSilent, cp)) {
      return (kindDropSilent, -1, null);
    }

    // An unmapped script, for example CJK or Arabic. Keep it. It then blocks a
    // match instead of joining two words together.
    return (kindHard, cp, null);
  }

  static (int, int, String?) _split(String value, int kind) {
    if (value.length == 1) {
      return (kind, value.codeUnitAt(0), null);
    }
    return (kind, -1, value);
  }

  static bool _inRanges(List<List<int>> ranges, int cp) {
    for (final List<int> r in ranges) {
      if (cp >= r[0] && cp <= r[1]) {
        return true;
      }
    }
    return false;
  }

  /// Folds a whole string and records where each folded character came from.
  static NormalizedText normalize(String text) {
    final List<int> chars = <int>[];
    final List<int> srcStart = <int>[];
    final List<int> srcEnd = <int>[];
    final List<bool> hard = <bool>[];
    final List<bool> gap = <bool>[];

    // The start of the text counts as a word break.
    bool pendingGap = true;
    int i = 0;

    while (i < text.length) {
      int width = 1;
      int cp = text.codeUnitAt(i);
      if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < text.length) {
        final int low = text.codeUnitAt(i + 1);
        if (low >= 0xDC00 && low <= 0xDFFF) {
          cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
          width = 2;
        }
      }

      final (int kind, int single, String? multi) = classify(cp);

      if (kind == kindDropBreak) {
        pendingGap = true;
      } else if (kind != kindDropSilent) {
        final bool isHard = kind == kindHard;
        final int start = i;
        final int end = i + width;

        if (multi == null) {
          chars.add(single);
          srcStart.add(start);
          srcEnd.add(end);
          hard.add(isHard);
          gap.add(pendingGap);
        } else {
          for (int k = 0; k < multi.length; k++) {
            chars.add(multi.codeUnitAt(k));
            srcStart.add(start);
            srcEnd.add(end);
            hard.add(isHard);
            gap.add(k == 0 && pendingGap);
          }
        }
        pendingGap = false;
      }

      i += width;
    }

    return NormalizedText(chars, srcStart, srcEnd, hard, gap);
  }

  /// Collapses every run of one character down to a single character.
  ///
  /// Returns null when the input holds no run. A null result means the caller
  /// can skip the second scan.
  static NormalizedText? squeeze(NormalizedText source) {
    final int length = source.length;
    bool hasRun = false;
    for (int i = 1; i < length; i++) {
      if (source.chars[i] == source.chars[i - 1]) {
        hasRun = true;
        break;
      }
    }

    if (!hasRun) {
      return null;
    }

    final List<int> chars = <int>[];
    final List<int> srcStart = <int>[];
    final List<int> srcEnd = <int>[];
    final List<bool> hard = <bool>[];
    final List<bool> gap = <bool>[];
    final List<int> runLength = <int>[];

    for (int i = 0; i < length; i++) {
      final int last = chars.length - 1;
      if (last >= 0 && chars[last] == source.chars[i]) {
        // Extend the run. The kept character now spans the whole run.
        srcEnd[last] = source.srcEnd[i];
        runLength[last]++;
        continue;
      }

      chars.add(source.chars[i]);
      srcStart.add(source.srcStart[i]);
      srcEnd.add(source.srcEnd[i]);
      hard.add(source.hard[i]);
      gap.add(source.gap[i]);
      runLength.add(1);
    }

    return NormalizedText(chars, srcStart, srcEnd, hard, gap,
        runLength: runLength);
  }

  /// Folds a term down to the plain character sequence the trie stores.
  ///
  /// Callers use this on a term they add at run time, so caller input and seed
  /// data reach the trie in the same form.
  static List<int> foldTerm(String term) => normalize(term).chars;

  /// Folds a term and collapses every run down to a single character.
  ///
  /// This is the form a term takes in the squeezed trie, so that a squeezed
  /// stream is scanned with squeezed patterns. Without it no term holding a
  /// doubled letter could ever match in the repeat-tolerant pass.
  static List<int> squeezeTerm(String term) {
    final List<int> folded = foldTerm(term);
    final List<int> out = <int>[];
    for (final int c in folded) {
      if (out.isEmpty || out[out.length - 1] != c) {
        out.add(c);
      }
    }
    return out;
  }

  /// The length of each run in a folded term, aligned with [squeezeTerm].
  ///
  /// The squeezed pass compares these against the runs the squeeze collapsed.
  /// Text may repeat a letter more often than the term does, never less, so
  /// one squeezed spelling cannot stand in for a different real word.
  static List<int> termRuns(String term) {
    final List<int> folded = foldTerm(term);
    final List<int> runs = <int>[];
    for (int i = 0; i < folded.length; i++) {
      if (i > 0 && folded[i] == folded[i - 1]) {
        runs[runs.length - 1]++;
      } else {
        runs.add(1);
      }
    }
    return runs;
  }

  /// Folds a term and returns it as a string.
  static String foldToString(String term) =>
      String.fromCharCodes(foldTerm(term));
}
