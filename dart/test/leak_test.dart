import 'dart:io';

import 'package:test/test.dart';
import 'package:vulgarity/languages.dart';
import 'package:vulgarity/src/pack_reader.dart';
import 'package:vulgarity/src/pack_text.dart';
import 'package:vulgarity/vulgarity.dart';

/// The point of the pack format, written as a test.
///
/// The term lists used to sit in `lib/` as 1.29 MB of readable JSON, which
/// pub.dev renders on the package page. They must not. The packs are a pure
/// function of the files in `data/`, so this test is exact and repeatable, not
/// probabilistic.
void main() {
  List<VulgarityTerm> allTerms() {
    final List<VulgarityTerm> all = <VulgarityTerm>[];
    for (final String code in kAvailableLanguages) {
      final List<VulgarityTerm> terms = <VulgarityTerm>[];
      loadPack(tryReadPackText(languageSeed(code))!, VulgarityFilter.profile,
          terms, <String>[]);
      all.addAll(terms);
    }
    return all;
  }

  bool isWordCharacter(String c) => RegExp(r'[A-Za-z0-9]').hasMatch(c);

  /// True when the term appears with no letter or digit on either side.
  bool hasWholeWord(String haystack, String term) {
    int at = haystack.indexOf(term);
    while (at >= 0) {
      final bool leftClear = at == 0 || !isWordCharacter(haystack[at - 1]);
      final int after = at + term.length;
      final bool rightClear =
          after >= haystack.length || !isWordCharacter(haystack[after]);
      if (leftClear && rightClear) {
        return true;
      }
      at = haystack.indexOf(term, at + 1);
    }
    return false;
  }

  test('no bundled pack holds a readable term', () {
    // Check the decoded payload, not the base64 text. Base64 is a wall of
    // letters, so a short run such as 'ass' turns up in it by chance; that is
    // noise, and the boundary-matched source scan below filters it. What
    // matters is that the bytes the payload decodes to hold no term at all.
    final List<VulgarityTerm> terms = allTerms();
    for (final String code in kAvailableLanguages) {
      final String payload =
          String.fromCharCodes(tryReadPackText(languageSeed(code))!);
      for (final VulgarityTerm term in terms) {
        expect(payload.contains(term.text), isFalse,
            reason: "the '$code' pack holds the readable term '${term.text}'");
      }
    }
  });

  test('no source file under lib/ holds a readable term', () {
    final Directory lib = Directory('lib');
    if (!lib.existsSync()) {
      fail('Cannot find lib/, so this test cannot check it. Run from dart/.');
    }

    // Severity 1 is allowed. The documentation has to name a real term to be
    // truthful, and the mechanism comments use 'damn', 'hell' and 'crap' for
    // exactly that. Severity 2 and above must not appear anywhere in lib/.
    final List<String> terms = allTerms()
        .where((VulgarityTerm t) => t.severity > 1 && t.text.length >= 4)
        .map((VulgarityTerm t) => t.text)
        .toSet()
        .toList();
    final Map<String, List<String>> leaked = <String, List<String>>{};

    for (final FileSystemEntity entity in lib.listSync(recursive: true)) {
      if (entity is! File || !entity.path.endsWith('.dart')) {
        continue;
      }
      // Match on a word boundary. Source is full of ordinary English
      // identifiers, and a plain substring search reports every one that
      // happens to contain a term, such as 'arse' inside 'parse'.
      final String text = entity.readAsStringSync().toLowerCase();
      final List<String> hits =
          terms.where((String t) => hasWholeWord(text, t)).toList();
      if (hits.isNotEmpty) {
        leaked[entity.path] = hits;
      }
    }

    expect(leaked, isEmpty,
        reason: 'These files hold readable terms of severity 2 or above:\n'
            '${leaked.entries.map((MapEntry<String, List<String>> e) => '  ${e.key}: ${e.value.length}').join('\n')}');
  });
}
