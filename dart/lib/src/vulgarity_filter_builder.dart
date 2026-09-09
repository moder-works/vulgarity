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
    final List<VulgarityTerm> terms = <VulgarityTerm>[];
    final List<String> allow = <String>[];
    _readDocument(document, terms, allow);
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

  /// Reads one term list into [terms] and [allow], whichever format it is in.
  ///
  /// The format is settled from the first character that carries content, so a
  /// seed document never runs through the base64 normaliser first. That
  /// normaliser walks the whole string, and on a megabyte of JSON the walk was
  /// a quarter of the load.
  static void _readDocument(
    String document,
    List<VulgarityTerm> terms,
    List<String> allow,
  ) {
    final int start = _firstContent(document);

    if (start < document.length && document.codeUnitAt(start) == 0x7B) {
      // jsonDecode takes leading blanks but not a BOM, so hand it the document
      // from its first real character. The copy only happens when there was
      // something to skip.
      loadSeed(start == 0 ? document : document.substring(start), kFoldProfile,
          terms, allow);
      return;
    }

    // "VPK1" in base64 is "VlBLMQ", and the magic is never masked, so every
    // pack starts with those six characters.
    if (document.startsWith('VlBLMQ', start)) {
      final List<int>? pack = tryReadPackText(document);
      if (pack != null) {
        loadPack(pack, kFoldProfile, terms, allow);
        return;
      }
    }

    throw const FormatException(
      'This is neither a seed document nor a pack. A seed document starts '
      "with '{'. A pack is base64 text starting with 'VlBLMQ'.",
    );
  }

  /// The index of the first character that is neither blank nor a BOM.
  ///
  /// The blank set matches the one [normalizePackText] drops, so both agree on
  /// where a document begins.
  static int _firstContent(String text) {
    int i = 0;
    while (i < text.length) {
      final int c = text.codeUnitAt(i);
      final bool blank = c == 0x20 || (c >= 0x09 && c <= 0x0D) || c == 0xFEFF;
      if (!blank) {
        return i;
      }
      i++;
    }
    return i;
  }

  /// Commits a whole term list. Every term is folded first, so a list with one
  /// unusable term leaves the builder untouched.
  void _absorb(List<VulgarityTerm> terms, List<String> allow) {
    for (final VulgarityTerm term in _foldAll(terms)) {
      _merge(term, canWiden: true);
    }
    for (final String word in allow) {
      addAllow(word);
    }
  }

  /// Adds a whole policy: its language packs, terms, allowlist and options.
  ///
  /// [preset] is a JSON document or a [VulgarityPreset]. Anything else is an
  /// [ArgumentError].
  ///
  /// The preset's options apply when you call [build] with no options of your
  /// own. Adding a second preset replaces the first one's options; the terms of
  /// both stay.
  ///
  /// This is all or nothing. Every list is resolved and read, and every term is
  /// folded, before the builder is touched at all. A preset that names two
  /// languages and cannot serve the second leaves the first unloaded, and a
  /// [languageResolver] that throws leaves the builder exactly as it was.
  ///
  /// A preset entry can make a term already loaded stricter or more severe,
  /// never wider. The language lists a preset names carry no such limit,
  /// because those are lists you chose to load.
  ///
  /// This package compiles in English only, so a preset that names any other
  /// language needs [languageResolver]. Pass `languageSeed` from
  /// `package:vulgarity/languages.dart`, or your own function.
  ///
  /// This differs from the .NET port. There, `VulgarityPreset.Parse` checks
  /// each code against the packs the assembly carries and refuses an unknown
  /// one at parse time. Here the parser checks the shape of a code only, and a
  /// code this package cannot serve fails at this call instead — which is what
  /// lets a [languageResolver] serve codes no bundled list covers. A document
  /// naming, say, `"pt"` therefore throws [FormatException] on .NET and
  /// [ArgumentError] here.
  void addPreset(
    Object preset, {
    String Function(String code)? languageResolver,
  }) {
    final VulgarityPreset parsed;
    if (preset is VulgarityPreset) {
      parsed = preset;
    } else if (preset is String) {
      parsed = VulgarityPreset.parse(preset);
    } else {
      throw ArgumentError.value(
          preset, 'preset', 'Pass a JSON string or a VulgarityPreset.');
    }

    // Stage the whole policy first. Resolving a list, reading it and folding
    // its terms can all fail, and a policy that fails halfway would otherwise
    // leave the builder holding part of it.
    //
    // The lists stay apart from the entries, because the two commit under
    // different merge rules. See [_merge].
    final List<VulgarityTerm> stagedLists = <VulgarityTerm>[];
    final List<String> stagedAllow = <String>[];

    for (final String code in parsed.languages) {
      if (code == 'en' && languageResolver == null) {
        _readDocument(kSeedEn, stagedLists, stagedAllow);
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
      _readDocument(languageResolver(code), stagedLists, stagedAllow);
    }

    stagedAllow.addAll(parsed.allow);

    // The last steps that can fail. Past here nothing throws.
    final List<VulgarityTerm> lists = _foldAll(stagedLists);
    final List<VulgarityTerm> entries = _foldAll(parsed.entries);

    // Order matters. Load the lists, add on top, then take away. A list is a
    // source the app author chose, so it may widen a term. An entry came with
    // the policy, so it may not.
    for (final VulgarityTerm term in lists) {
      _merge(term, canWiden: true);
    }
    for (final VulgarityTerm term in entries) {
      _merge(term, canWiden: false);
    }
    for (final String word in stagedAllow) {
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

    final AhoCorasick allowTrie = AhoCorasick();
    for (final String word in _allow) {
      allowTrie.add(TextNormalizer.foldTerm(word));
    }
    allowTrie.build();

    return VulgarityFilter.internal(terms, termTrie, allowTrie, effective);
  }

  /// Folds one term. Returns null when nothing is left of it.
  static VulgarityTerm? _fold(VulgarityTerm term) {
    final String folded = TextNormalizer.foldToString(term.text);
    if (folded.isEmpty) {
      return null;
    }
    return folded == term.text
        ? term
        : VulgarityTerm(
            folded, term.categoryName, term.severity, term.requireBoundary);
  }

  /// Folds a whole list, or throws before it folds any of it into the builder.
  ///
  /// A term that folds to nothing came from a document here, not from a call to
  /// [addTerm], so it is a [FormatException]: the document is malformed.
  static List<VulgarityTerm> _foldAll(List<VulgarityTerm> terms) {
    final List<VulgarityTerm> folded = <VulgarityTerm>[];
    for (final VulgarityTerm term in terms) {
      final VulgarityTerm? one = _fold(term);
      if (one == null) {
        throw FormatException("The term '${term.text}' folds to nothing under "
            'profile $kFoldProfile, so it could never match.');
      }
      folded.add(one);
    }
    return folded;
  }

  /// Folds a term and stores it. Throws when the term folds to nothing.
  ///
  /// This is the direct path, from [addTerm]. A caller who names a term in
  /// code got the argument wrong, so this stays an [ArgumentError].
  void _register(VulgarityTerm term) {
    final VulgarityTerm? folded = _fold(term);
    if (folded == null) {
      throw ArgumentError.value(
          term.text, 'term', 'This term folds to nothing.');
    }
    _merge(folded, canWiden: true);
  }

  /// Stores an already-folded term, keeping the worse of any pair.
  ///
  /// This never throws, which is what lets [addPreset] and [_absorb] commit a
  /// staged list knowing that nothing can fail halfway.
  ///
  /// Two sources gave the same term. The rating always takes the worse of the
  /// two, and the category follows the higher severity. What differs is the
  /// boundary rule, and it differs by who is asking:
  ///
  ///  * [canWiden] is true for a term list and for [addTerm] — every source the
  ///    app author chose. There the wider rule wins (`existing && incoming`).
  ///    Several bundled lists carry a term the English list also carries, and
  ///    carry it WITH a boundary where English has none. Loading a second
  ///    language must not narrow the first, or a compound the English list used
  ///    to catch would go quiet the moment a language was added.
  ///  * [canWiden] is false for the `entries` of a preset — the one source that
  ///    can arrive from the network. There the boundary is sticky
  ///    (`existing || incoming`) and the severity may only rise, so a remote
  ///    policy can make a bundled term stricter but never looser. That is what
  ///    stops an entry `{"t":"ass","sev":1}` from making "class" match.
  void _merge(VulgarityTerm incoming, {required bool canWiden}) {
    final String folded = incoming.text;
    final VulgarityTerm? existing = _terms[folded];
    if (existing == null) {
      _terms[folded] = incoming;
      _termOrder.add(folded);
      return;
    }

    final bool boundary = canWiden
        ? existing.requireBoundary && incoming.requireBoundary
        : existing.requireBoundary || incoming.requireBoundary;

    if (incoming.severity > existing.severity) {
      // The category follows the higher severity.
      _terms[folded] = VulgarityTerm(
          folded, incoming.categoryName, incoming.severity, boundary);
    } else if (boundary != existing.requireBoundary) {
      _terms[folded] = VulgarityTerm(
          folded, existing.categoryName, existing.severity, boundary);
    }
  }
}
