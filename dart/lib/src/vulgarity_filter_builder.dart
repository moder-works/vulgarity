import 'model/vulgarity_term.dart';
import 'normalization/fold_table.g.dart';
import 'normalization/text_normalizer.dart';
import 'seed_data.g.dart';
import 'seed_loader.dart';
import 'trie/aho_corasick.dart';
import 'vulgarity_filter.dart';
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

  /// Adds the bundled English term list.
  void useDefaultSeed() => addSeed(kSeedEn);

  /// Adds every term from one seed document.
  ///
  /// Pass a language pack constant here, for example `seedEs` from
  /// `package:vulgarity/lang/es.dart`.
  void addSeed(String json) {
    final List<VulgarityTerm> terms = <VulgarityTerm>[];
    final List<String> allow = <String>[];
    loadSeed(json, kFoldProfile, terms, allow);

    for (final VulgarityTerm term in terms) {
      _register(term);
    }
    for (final String word in allow) {
      addAllow(word);
    }
  }

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
    final VulgarityOptions effective = options ?? VulgarityOptions();
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

    final AhoCorasick allowTrie = AhoCorasick();
    for (final String word in _allow) {
      allowTrie.add(TextNormalizer.foldTerm(word));
    }
    allowTrie.build();

    return VulgarityFilter.internal(terms, termTrie, allowTrie, effective);
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
