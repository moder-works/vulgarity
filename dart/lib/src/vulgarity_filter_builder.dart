import 'model/vulgarity_term.dart';
import 'normalization/fold_table.g.dart';
import 'normalization/text_normalizer.dart';
import 'pack_reader.dart';
import 'pack_text.dart';
import 'seed_data.g.dart';
import 'seed_loader.dart';
import 'trie/aho_corasick.dart';
import 'vulgarity_filter.dart';
import 'vulgarity_preset.dart';
import 'vulgarity_options.dart';

/// Collects terms from one or more sources, then compiles a filter.
///
/// ```dart
/// import 'package:vulgarity/vulgarity.dart';
/// import 'package:vulgarity/lang/es.dart';
///
/// final filter = (VulgarityFilterBuilder()
///       ..useDefaultSeed()
///       ..addSeed(seedEs)
///       ..addTerm('brandname', 'profanity', 2, true)
///       ..addAllow('scunthorpe'))
///     .build();
/// ```
class VulgarityFilterBuilder {
  final Map<String, VulgarityTerm> _terms = <String, VulgarityTerm>{};
  final List<String> _termOrder = <String>[];
  final Set<String> _allow = <String>{};
  VulgarityOptions? _presetOptions;

  /// Adds the bundled English term list.
  void useDefaultSeed() => addSeed(kSeedEn);

  /// Adds every term from one term list.
  ///
  /// [document] is a seed document as JSON, or a pack as base64 text. This
  /// reads the format from the text itself, so a caller never has to say which
  /// it is. Pass a language pack constant here, for example `seedEs` from
  /// `package:vulgarity/lang/es.dart`.
  ///
  /// Build a pack of your own with: `python3 tool/pack.py my-list.json`
  void addSeed(String document) {
    final List<int>? pack = tryReadPackText(document);
    if (pack != null) {
      addSeedBytes(pack);
      return;
    }

    if (!_looksLikeJson(document)) {
      throw const FormatException(
        'This is neither a seed document nor a pack. A seed document starts '
        "with '{'. A pack is base64 text starting with 'VlBLMQ'.",
      );
    }

    final List<VulgarityTerm> terms = <VulgarityTerm>[];
    final List<String> allow = <String>[];
    loadSeed(document, kFoldProfile, terms, allow);
    _absorb(terms, allow);
  }

  /// Adds every term from one pack.
  ///
  /// Use this when you host your own list and want no readable term in transit
  /// or in a cache. Build one with `tool/pack.py`.
  void addSeedBytes(List<int> pack) {
    final List<VulgarityTerm> terms = <VulgarityTerm>[];
    final List<String> allow = <String>[];
    loadPack(pack, kFoldProfile, terms, allow);
    _absorb(terms, allow);
  }

  void _absorb(List<VulgarityTerm> terms, List<String> allow) {
    for (final VulgarityTerm term in terms) {
      _register(term);
    }
    for (final String word in allow) {
      addAllow(word);
    }
  }

  /// True when the first character that is not blank is an opening brace.
  static bool _looksLikeJson(String text) {
    for (int i = 0; i < text.length; i++) {
      final String c = text[i];
      if (c.trim().isNotEmpty && c != '\u{FEFF}') {
        return c == '{';
      }
    }
    return false;
  }

  /// Adds a whole policy: its language packs, terms, allowlist and options.
  ///
  /// The preset's options apply when you call [build] with no options of your
  /// own. Adding a second preset replaces the first one's options; the terms of
  /// both stay.
  ///
  /// This package compiles in English only, so a preset that names any other
  /// language needs [languageResolver]. Pass `languageSeed` from
  /// `package:vulgarity/languages.dart`, or your own function.
  void addPreset(
    Object preset, {
    String Function(String code)? languageResolver,
  }) {
    final VulgarityPreset parsed = preset is VulgarityPreset
        ? preset
        : VulgarityPreset.parse(preset as String);

    // Order matters. Load the lists, add on top, then take away.
    for (final String code in parsed.languages) {
      if (code == 'en' && languageResolver == null) {
        useDefaultSeed();
        continue;
      }
      if (languageResolver == null) {
        throw ArgumentError.value(
          code,
          'preset.languages',
          "This package compiles in English only. To load '$code', pass "
              'languageResolver: languageSeed from '
              'package:vulgarity/languages.dart, or import the pack and '
              'resolve it yourself.',
        );
      }
      addSeed(languageResolver(code));
    }

    for (final VulgarityTerm term in parsed.entries) {
      _register(term);
    }
    for (final String word in parsed.allow) {
      addAllow(word);
    }
    for (final String term in parsed.remove) {
      removeTerm(term);
    }

    _presetOptions = parsed.options;
  }

  /// Drops one term, if it is present.
  ///
  /// A term that is absent is not an error. That keeps a remote policy working
  /// against an older term list.
  void removeTerm(String term) {
    if (term.isEmpty) {
      return;
    }
    final String folded = TextNormalizer.foldToString(term);
    if (_terms.remove(folded) != null) {
      _termOrder.remove(folded);
    }
  }

  /// Reports whether the builder currently holds a term.
  bool hasTerm(String term) =>
      term.isNotEmpty && _terms.containsKey(TextNormalizer.foldToString(term));

  /// How many terms the builder currently holds.
  int get termCount => _termOrder.length;

  /// Adds one term.
  ///
  /// The builder folds [term], so any spelling works. [severity] runs 1 to 5.
  /// Set [requireBoundary] to demand a word boundary around a match.
  void addTerm(
      String term, String category, int severity, bool requireBoundary) {
    if (severity < 1 || severity > 5) {
      throw RangeError.range(severity, 1, 5, 'severity');
    }
    _register(VulgarityTerm(term, category, severity, requireBoundary));
  }

  /// Adds one innocent word that holds a term, so the filter never flags it.
  void addAllow(String word) {
    if (word.isEmpty) {
      return;
    }
    final String folded = TextNormalizer.foldToString(word);
    if (folded.isNotEmpty) {
      _allow.add(folded);
    }
  }

  /// Compiles the trie and returns the filter.
  VulgarityFilter build([VulgarityOptions? options]) {
    // Your own options win. Otherwise the last preset's options apply.
    final VulgarityOptions effective =
        options ?? _presetOptions ?? VulgarityOptions();
    effective.validate();

    if (_termOrder.isEmpty) {
      throw StateError('Add at least one term. '
          'Call useDefaultSeed to load the bundled list.');
    }

    final List<VulgarityTerm> terms = <VulgarityTerm>[];
    final AhoCorasick termTrie = AhoCorasick();

    for (int i = 0; i < _termOrder.length; i++) {
      final VulgarityTerm term = _terms[_termOrder[i]]!;
      terms.add(term);

      final int id = termTrie.add(TextNormalizer.foldTerm(term.text));
      if (id != i) {
        throw StateError('The trie assigned an unexpected id. '
            "Term '${term.text}' is a duplicate.");
      }
    }

    termTrie.build();

    // A second trie over the squeezed spelling of every term. The repeat pass
    // scans a stream whose runs are collapsed, so it has to carry patterns whose
    // runs are collapsed too, or no term holding a doubled letter could match.
    // Several terms can share one squeezed spelling, so the map is one to many.
    final AhoCorasick squeezedTrie = AhoCorasick();
    final List<List<int>> squeezedTerms = <List<int>>[];
    final List<List<int>> termRuns = <List<int>>[];

    for (int i = 0; i < terms.length; i++) {
      final int id =
          squeezedTrie.add(TextNormalizer.squeezeTerm(terms[i].text));
      while (squeezedTerms.length <= id) {
        squeezedTerms.add(<int>[]);
      }
      squeezedTerms[id].add(i);
      termRuns.add(TextNormalizer.termRuns(terms[i].text));
    }

    squeezedTrie.build();

    final AhoCorasick allowTrie = AhoCorasick();
    for (final String word in _allow) {
      allowTrie.add(TextNormalizer.foldTerm(word));
    }
    allowTrie.build();

    return VulgarityFilter.internal(
      terms,
      termTrie,
      allowTrie,
      effective,
      squeezedTrie,
      squeezedTerms,
      termRuns,
    );
  }

  /// Stores a term under its folded form, keeping the worse of any pair.
  void _register(VulgarityTerm term) {
    final String folded = TextNormalizer.foldToString(term.text);
    if (folded.isEmpty) {
      throw ArgumentError.value(
          term.text, 'term', 'This term folds to nothing.');
    }

    final VulgarityTerm normalized = folded == term.text
        ? term
        : VulgarityTerm(
            folded, term.categoryName, term.severity, term.requireBoundary);

    final VulgarityTerm? existing = _terms[folded];
    if (existing != null) {
      // Two sources gave the same term. Keep the worse rating, and keep the
      // wider match rule, so no source loses coverage.
      if (normalized.severity > existing.severity ||
          !normalized.requireBoundary) {
        _terms[folded] = VulgarityTerm(
          folded,
          existing.severity >= normalized.severity
              ? existing.categoryName
              : normalized.categoryName,
          existing.severity > normalized.severity
              ? existing.severity
              : normalized.severity,
          existing.requireBoundary && normalized.requireBoundary,
        );
      }
      return;
    }

    _terms[folded] = normalized;
    _termOrder.add(folded);
  }
}
