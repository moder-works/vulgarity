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
  );

  final List<VulgarityTerm> _terms;
  final AhoCorasick _termTrie;
  final AhoCorasick _allowTrie;

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
    return VulgarityFilter.internal(_terms, _termTrie, _allowTrie, options);
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

    final List<VulgarityMatch> matches = <VulgarityMatch>[];
    _collectTerms(streamA, allowStart, allowEnd, matches, stopAtFirst);
    final int fromStreamA = matches.length;

    if (streamB != null && !(stopAtFirst && matches.isNotEmpty)) {
      // The squeeze pass is a fallback, not a second opinion. It widens a span
      // across the letters it collapsed, so "damn the music crap" would report
      // "c crap" for the second term. Stream A already holds the tight span,
      // so drop the loose duplicate.
      final List<VulgarityMatch> squeezed = <VulgarityMatch>[];
      _collectTerms(streamB, allowStart, allowEnd, squeezed, stopAtFirst);

      for (final VulgarityMatch candidate in squeezed) {
        if (!_overlapsSameTerm(matches, fromStreamA, candidate)) {
          matches.add(candidate);
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

  void _collectTerms(
    NormalizedText stream,
    List<int> allowStart,
    List<int> allowEnd,
    List<VulgarityMatch> matches,
    bool stopAtFirst,
  ) {
    final List<RawHit> hits = <RawHit>[];
    _termTrie.scan(stream, hits);
    final Set<VulgarityCategory>? categories = options.categories;

    for (final RawHit hit in hits) {
      final int termIndex = hit.patternId;
      final VulgarityTerm term = _terms[termIndex];

      if (term.severity < options.minSeverity) {
        continue;
      }
      if (categories != null && !categories.contains(term.category)) {
        continue;
      }

      final int endIndex = hit.end;
      final int startIndex = endIndex - _termTrie.lengthOf(termIndex) + 1;
      if (startIndex < 0) {
        continue;
      }

      if (term.requireBoundary &&
          !_hasWordBoundary(stream, startIndex, endIndex)) {
        continue;
      }

      final int start = stream.srcStart[startIndex];
      final int end = stream.srcEnd[endIndex];

      if (_isAllowed(allowStart, allowEnd, start, end)) {
        continue;
      }

      matches.add(VulgarityMatch(start, end, term, termIndex));
      if (stopAtFirst) {
        return;
      }
    }
  }

  /// Tests whether a match sits on a word boundary.
  ///
  /// An edge is a boundary when the match reaches the end of the text, when a
  /// separator was dropped there, or when the neighbouring character is not a
  /// real word character. The gap test is what lets "a hell" match while
  /// "shell" does not.
  static bool _hasWordBoundary(
      NormalizedText stream, int startIndex, int endIndex) {
    final bool leftOk = startIndex == 0 ||
        stream.gap[startIndex] ||
        !stream.hard[startIndex - 1];

    if (!leftOk) {
      return false;
    }

    final int after = endIndex + 1;
    return after >= stream.length || stream.gap[after] || !stream.hard[after];
  }

  /// Reports whether stream A already found this term across this span.
  static bool _overlapsSameTerm(
      List<VulgarityMatch> matches, int count, VulgarityMatch candidate) {
    for (int i = 0; i < count; i++) {
      final VulgarityMatch found = matches[i];
      if (found.termIndex != candidate.termIndex) {
        continue;
      }
      if (found.start < candidate.end && candidate.start < found.end) {
        return true;
      }
    }
    return false;
  }

  static bool _isAllowed(List<int> starts, List<int> ends, int start, int end) {
    for (int i = 0; i < starts.length; i++) {
      if (starts[i] <= start && end <= ends[i]) {
        return true;
      }
    }
    return false;
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
