import 'model/vulgarity_category.dart';
import 'model/vulgarity_match.dart';
import 'model/vulgarity_term.dart';
import 'normalization/fold_table.g.dart';
import 'normalization/normalized_text.dart';
import 'normalization/text_normalizer.dart';
import 'trie/aho_corasick.dart';
import 'vulgarity_filter_builder.dart';
import 'vulgarity_options.dart';

/// Finds vulgar terms in text, masks them, and scores them.
///
/// Build one filter and keep it. The constructor compiles the term list into a
/// trie, which costs far more than a scan. An instance holds no mutable state.
///
/// ```dart
/// final filter = VulgarityFilter.createDefault();
/// final bool dirty = filter.detect(comment);
/// final String clean = filter.filter(comment);
/// ```
class VulgarityFilter {
  const VulgarityFilter.internal(
    this._terms,
    this._termTrie,
    this._allowTrie,
    this.options,
    this._squeezedTrie,
    this._squeezedTerms,
    this._termRuns,
  );

  final List<VulgarityTerm> _terms;
  final AhoCorasick _termTrie;
  final AhoCorasick _allowTrie;

  /// Every term again, with its runs collapsed. The repeat pass scans with it.
  final AhoCorasick _squeezedTrie;

  /// Maps a pattern in [_squeezedTrie] to every term that squeezes onto it.
  final List<List<int>> _squeezedTerms;

  /// The run lengths of each term's folded spelling, one list per term.
  final List<List<int>> _termRuns;

  /// The options this filter runs with.
  final VulgarityOptions options;

  /// The fold profile this build implements.
  static String get profile => kFoldProfile;

  /// How many terms the filter holds.
  int get termCount => _terms.length;

  /// Builds a filter from the bundled English term list.
  factory VulgarityFilter.createDefault([VulgarityOptions? options]) {
    return (VulgarityFilterBuilder()..useDefaultSeed()).build(options);
  }

  /// Builds a filter from one seed document.
  factory VulgarityFilter.fromSeed(String json, [VulgarityOptions? options]) {
    return (VulgarityFilterBuilder()..addSeed(json)).build(options);
  }

  /// Builds a filter from a preset document or a [VulgarityPreset].
  ///
  /// A preset carries the policy as well as the terms, so a server can change
  /// how strict a client is without an app release. Treat a preset from the
  /// network as untrusted: this throws [FormatException] on a malformed
  /// document, and it never partly applies one.
  ///
  /// This package compiles in English only. A preset that names any other
  /// language needs [languageResolver] — pass `languageSeed` from
  /// `package:vulgarity/languages.dart`.
  ///
  /// Where a bad language code surfaces differs from the .NET port. There,
  /// `VulgarityPreset.Parse` resolves each code against the packs the assembly
  /// carries and throws [FormatException] at parse time. Here the parser checks
  /// only that a code is 2 to 8 lower-case letters, and a code that nothing can
  /// serve throws [ArgumentError] from this call instead. That is what lets a
  /// [languageResolver] serve codes no bundled list covers.
  ///
  /// Throws [ArgumentError] when [preset] is neither a JSON string nor a
  /// [VulgarityPreset].
  ///
  /// ```dart
  /// final response = await http.get(Uri.parse('https://example.com/policy.json'));
  /// final filter = VulgarityFilter.fromPreset(response.body);
  /// ```
  factory VulgarityFilter.fromPreset(
    Object preset, {
    String Function(String code)? languageResolver,
  }) {
    return (VulgarityFilterBuilder()
          ..addPreset(preset, languageResolver: languageResolver))
        .build();
  }

  /// Returns a filter with different options. It reuses the compiled trie.
  VulgarityFilter withOptions(VulgarityOptions options) {
    options.validate();
    return VulgarityFilter.internal(
      _terms,
      _termTrie,
      _allowTrie,
      options,
      _squeezedTrie,
      _squeezedTerms,
      _termRuns,
    );
  }

  /// Reports whether the text holds any term. It stops at the first one.
  bool detect(String? text) {
    if (text == null || text.isEmpty) {
      return false;
    }
    return _collect(text, true).isNotEmpty;
  }

  /// Finds every term in the text.
  ///
  /// The matches arrive ordered by start. Each one carries offsets into the
  /// ORIGINAL text, so a caller can highlight the real span.
  List<VulgarityMatch> scan(String? text) {
    if (text == null || text.isEmpty) {
      return const <VulgarityMatch>[];
    }
    return _collect(text, false);
  }

  /// Replaces every match with the mask.
  String? filter(String? text) {
    if (text == null || text.isEmpty) {
      return text;
    }

    final List<VulgarityMatch> matches = _collect(text, false);
    if (matches.isEmpty) {
      return text;
    }

    final StringBuffer buffer = StringBuffer();
    int cursor = 0;
    int i = 0;

    while (i < matches.length) {
      final int start = matches[i].start;
      int end = matches[i].end;

      // Merge the overlapping matches, so no character is masked twice.
      int j = i + 1;
      while (j < matches.length && matches[j].start < end) {
        if (matches[j].end > end) {
          end = matches[j].end;
        }
        j++;
      }

      buffer.write(text.substring(cursor, start));
      final String? token = options.maskToken;
      buffer.write(token ?? options.maskChar * (end - start));
      cursor = end;
      i = j;
    }

    buffer.write(text.substring(cursor));
    return buffer.toString();
  }

  /// Scores the text from the severity of its matches.
  ///
  /// Returns zero when the text holds no term.
  int score(String? text) {
    if (text == null || text.isEmpty) {
      return 0;
    }

    final List<VulgarityMatch> matches = _collect(text, false);
    int result = 0;

    for (final VulgarityMatch match in matches) {
      if (options.scoreMode == ScoreMode.max) {
        if (match.severity > result) {
          result = match.severity;
        }
      } else {
        result += match.severity;
      }
    }

    return result;
  }

  // ---------------------------------------------------------------------
  // The scan itself
  // ---------------------------------------------------------------------

  List<VulgarityMatch> _collect(String text, bool stopAtFirst) {
    final NormalizedText streamA = TextNormalizer.normalize(text);
    final NormalizedText? streamB =
        options.repeatTolerance ? TextNormalizer.squeeze(streamA) : null;

    // The allowlist must be complete before any term match is judged.
    final List<int> allowStart = <int>[];
    final List<int> allowEnd = <int>[];
    _collectAllow(streamA, allowStart, allowEnd);
    if (streamB != null) {
      _collectAllow(streamB, allowStart, allowEnd);
    }
    final _SpanIndex allow = _SpanIndex.of(allowStart, allowEnd);

    final List<VulgarityMatch> matches = <VulgarityMatch>[];
    _collectTerms(streamA, _termTrie, null, allow, matches, stopAtFirst);

    if (streamB != null && !(stopAtFirst && matches.isNotEmpty)) {
      // The squeeze pass is a fallback, not a second opinion. It widens a span
      // across the letters it collapsed, so "damn the music crap" would report
      // "c crap" for the second term. Stream A already holds the tight span,
      // so drop every loose candidate that lands on one.
      final List<VulgarityMatch> squeezed = <VulgarityMatch>[];
      _collectTerms(
          streamB, _squeezedTrie, _squeezedTerms, allow, squeezed, stopAtFirst);

      if (squeezed.isNotEmpty) {
        final _SpanIndex fromStreamA = _SpanIndex.ofMatches(matches);
        for (final VulgarityMatch candidate in squeezed) {
          if (!fromStreamA.overlaps(candidate.start, candidate.end)) {
            matches.add(candidate);
          }
        }
      }
    }

    if (matches.length < 2) {
      return matches;
    }

    matches.sort();
    _removeDuplicates(matches);
    if (options.collapseContained) {
      _removeContained(matches);
    }

    return matches;
  }

  void _collectAllow(NormalizedText stream, List<int> starts, List<int> ends) {
    if (_allowTrie.patternCount == 0) {
      return;
    }

    final List<RawHit> hits = <RawHit>[];
    _allowTrie.scan(stream, hits);

    for (final RawHit hit in hits) {
      final int startIndex = hit.end - _allowTrie.lengthOf(hit.patternId) + 1;
      if (startIndex < 0) {
        continue;
      }
      starts.add(stream.srcStart[startIndex]);
      ends.add(stream.srcEnd[hit.end]);
    }
  }

  /// Turns raw hits into matches.
  ///
  /// [trie] carries the patterns, and [patternTerms] maps a pattern back to
  /// every term that produced it. Pass null for the plain pass, where a pattern
  /// id is already a term index. A non-null map marks the squeezed pass, where
  /// a term that actually lost a letter has to land on runs at least as long as
  /// its own.
  void _collectTerms(
    NormalizedText stream,
    AhoCorasick trie,
    List<List<int>>? patternTerms,
    _SpanIndex allow,
    List<VulgarityMatch> matches,
    bool stopAtFirst,
  ) {
    final List<RawHit> hits = <RawHit>[];
    trie.scan(stream, hits);
    final Set<VulgarityCategory>? categories = options.categories;

    for (final RawHit hit in hits) {
      final int patternId = hit.patternId;
      final int patternLength = trie.lengthOf(patternId);
      final int endIndex = hit.end;
      final int startIndex = endIndex - patternLength + 1;
      if (startIndex < 0) {
        continue;
      }

      // This holds for every term the pattern stands for, so pay for it once.
      final bool interiorGap = _hasInteriorGap(stream, startIndex, endIndex);

      final List<int>? mapped =
          patternTerms == null ? null : patternTerms[patternId];
      final int termCount = mapped == null ? 1 : mapped.length;

      for (int n = 0; n < termCount; n++) {
        final int termIndex = mapped == null ? patternId : mapped[n];
        final VulgarityTerm term = _terms[termIndex];

        if (term.severity < options.minSeverity) {
          continue;
        }
        if (categories != null && !categories.contains(term.category)) {
          continue;
        }

        // A term that squeezes onto a shorter spelling only matches where the
        // stream really did collapse the same runs. Text may repeat a letter
        // more often than the term does, never less, so a term spelled with a
        // doubled letter cannot stand in for a different real word that shares
        // its squeezed spelling.
        if (mapped != null &&
            patternLength != _termTrie.lengthOf(termIndex) &&
            !_runsCover(stream, startIndex, _termRuns[termIndex])) {
          continue;
        }

        if (term.requireBoundary) {
          if (!_hasLeftBoundary(stream, startIndex) ||
              !_hasRightBoundary(stream, endIndex)) {
            continue;
          }
        } else if (interiorGap && !_hasLeftBoundary(stream, startIndex)) {
          // The match swallowed a separator, so it spans two words. Only a term
          // that starts its own word may do that. Otherwise the tail of one
          // ordinary word joined to the head of the next spells a term, and a
          // harmless sentence gets flagged. The right edge stays free, so a
          // term written with a hyphen or a space between every letter still
          // matches.
          continue;
        }

        final int start = stream.srcStart[startIndex];
        final int end = stream.srcEnd[endIndex];

        if (allow.covers(start, end)) {
          continue;
        }

        matches.add(VulgarityMatch(start, end, term, termIndex));
        if (stopAtFirst) {
          return;
        }
      }
    }
  }

  /// True when a separator was dropped inside the match, not just before it.
  static bool _hasInteriorGap(
      NormalizedText stream, int startIndex, int endIndex) {
    for (int k = startIndex + 1; k <= endIndex; k++) {
      if (stream.gap[k]) {
        return true;
      }
    }
    return false;
  }

  /// True when every run the match landed on is as long as the term's own.
  static bool _runsCover(
      NormalizedText stream, int startIndex, List<int> runs) {
    for (int k = 0; k < runs.length; k++) {
      if (stream.runLengthAt(startIndex + k) < runs[k]) {
        return false;
      }
    }
    return true;
  }

  /// Tests the left edge of a match for a word boundary.
  ///
  /// An edge is a boundary when the match reaches the edge of the text, when a
  /// separator was dropped there, or when the neighbouring character is not a
  /// real word character. The gap test is what lets "a hell" match while
  /// "shell" does not.
  static bool _hasLeftBoundary(NormalizedText stream, int startIndex) =>
      startIndex == 0 || stream.gap[startIndex] || !stream.hard[startIndex - 1];

  /// Tests the right edge of a match for a word boundary.
  static bool _hasRightBoundary(NormalizedText stream, int endIndex) {
    final int after = endIndex + 1;
    return after >= stream.length || stream.gap[after] || !stream.hard[after];
  }

  static void _removeDuplicates(List<VulgarityMatch> matches) {
    int write = 1;
    for (int read = 1; read < matches.length; read++) {
      final VulgarityMatch previous = matches[write - 1];
      final VulgarityMatch current = matches[read];
      if (current.start == previous.start &&
          current.end == previous.end &&
          current.termIndex == previous.termIndex) {
        continue;
      }
      matches[write++] = current;
    }
    matches.removeRange(write, matches.length);
  }

  /// Drops a match that sits fully inside an earlier, longer match.
  ///
  /// The list arrives ordered by start, then by the longer match. So every kept
  /// match starts at or before the one under test, and comparing against the
  /// furthest end so far is enough.
  static void _removeContained(List<VulgarityMatch> matches) {
    int write = 0;
    int furthestEnd = -1;

    for (int read = 0; read < matches.length; read++) {
      if (matches[read].end <= furthestEnd) {
        continue;
      }
      furthestEnd = matches[read].end;
      matches[write++] = matches[read];
    }
    matches.removeRange(write, matches.length);
  }
}

/// A set of spans that answers containment and overlap in O(log n).
///
/// A scan of a large text finds a great many spans, and every match is judged
/// against all of them. Walking the list makes that quadratic: a megabyte of an
/// allowlisted word took seconds. The spans are sorted by start, and each entry
/// carries the furthest end seen up to it, so one binary search settles a
/// question that used to need a full sweep.
class _SpanIndex {
  const _SpanIndex._(this._start, this._maxEnd);

  final List<int> _start;
  final List<int> _maxEnd;

  static const _SpanIndex _empty = _SpanIndex._(<int>[], <int>[]);

  /// Builds an index over parallel start and end lists.
  factory _SpanIndex.of(List<int> starts, List<int> ends) {
    final int count = starts.length;
    if (count == 0) {
      return _empty;
    }

    final List<int> order = List<int>.generate(count, (int i) => i);
    order.sort((int a, int b) {
      final int byStart = starts[a] - starts[b];
      return byStart != 0 ? byStart : ends[a] - ends[b];
    });

    return _build(
        count, (int i) => starts[order[i]], (int i) => ends[order[i]]);
  }

  /// Builds an index over the span of every match.
  factory _SpanIndex.ofMatches(List<VulgarityMatch> matches) {
    final int count = matches.length;
    if (count == 0) {
      return _empty;
    }

    final List<VulgarityMatch> sorted = matches.toList()
      ..sort((VulgarityMatch a, VulgarityMatch b) {
        final int byStart = a.start - b.start;
        return byStart != 0 ? byStart : a.end - b.end;
      });

    return _build(count, (int i) => sorted[i].start, (int i) => sorted[i].end);
  }

  static _SpanIndex _build(
      int count, int Function(int) startAt, int Function(int) endAt) {
    final List<int> start = List<int>.filled(count, 0);
    final List<int> maxEnd = List<int>.filled(count, 0);

    int running = endAt(0);
    for (int i = 0; i < count; i++) {
      start[i] = startAt(i);
      final int end = endAt(i);
      if (end > running) {
        running = end;
      }
      maxEnd[i] = running;
    }

    return _SpanIndex._(start, maxEnd);
  }

  /// True when one span holds the whole of `[start, end)`.
  bool covers(int start, int end) {
    final int i = _lastStartAtOrBefore(start);
    return i >= 0 && _maxEnd[i] >= end;
  }

  /// True when one span shares a character with `[start, end)`.
  bool overlaps(int start, int end) {
    final int i = _lastStartBefore(end);
    return i >= 0 && _maxEnd[i] > start;
  }

  /// The last entry whose start is at or before [value], or -1.
  int _lastStartAtOrBefore(int value) {
    int low = 0;
    int high = _start.length - 1;
    int found = -1;

    while (low <= high) {
      final int mid = low + ((high - low) >> 1);
      if (_start[mid] <= value) {
        found = mid;
        low = mid + 1;
      } else {
        high = mid - 1;
      }
    }

    return found;
  }

  /// The last entry whose start is strictly before [value], or -1.
  int _lastStartBefore(int value) {
    int low = 0;
    int high = _start.length - 1;
    int found = -1;

    while (low <= high) {
      final int mid = low + ((high - low) >> 1);
      if (_start[mid] < value) {
        found = mid;
        low = mid + 1;
      } else {
        high = mid - 1;
      }
    }

    return found;
  }
}
