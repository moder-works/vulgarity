import 'package:test/test.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

/// The count detected when this fixture was recorded.
const int detectionBaseline = 2894;

/// Evasions the cross-word boundary rule gives up on, and why.
///
/// A term with no boundary rule may span a dropped separator only when it
/// starts its own word. Without that, the tail of one innocent word plus the
/// head of the next spells a term, and "wash it down" reports one. These
/// entries pay for that: the term sits in the middle of a longer word AND
/// straddles a separator, which is exactly the shape the rule rejects. Every
/// other one of the thousand-odd evasions still matches.
const Set<String> knownBoundaryLosses = <String>{'m.otherf.ucker'};

void main() {
  final VulgarityFilter filter = VulgarityFilter.createDefault();

  // The corpus never seeds the filter. It is unvetted, and it holds ordinary
  // words such as "aunt", "cool" and "acct". It earns its place as a test
  // fixture, because it carries thousands of genuine evasion spellings.

  test('every known evasion is detected', () {
    final List<dynamic> terms =
        readFixture('evasions-en.json')['terms'] as List<dynamic>;
    final List<String> missed = <String>[];

    for (final dynamic entry in terms) {
      final String term = entry as String;
      if (!filter.detect(term) && !knownBoundaryLosses.contains(term)) {
        missed.add(term);
      }
    }

    expect(terms.length, greaterThan(900),
        reason: 'the evasion fixture shrank unexpectedly');
    expect(missed, isEmpty,
        reason: 'The filter missed ${missed.length} of ${terms.length} '
            'known evasions.');
  });

  test('corpus detection does not regress', () {
    final List<dynamic> terms =
        readFixture('corpus-en.json')['terms'] as List<dynamic>;
    int detected = 0;
    for (final dynamic term in terms) {
      if (filter.detect(term as String)) {
        detected++;
      }
    }

    // The corpus is mostly noise, so full coverage is neither possible nor
    // wanted. This guards against a drop.
    expect(detected, greaterThanOrEqualTo(detectionBaseline - 20),
        reason: 'Corpus detection fell to $detected from a baseline of '
            '$detectionBaseline.');
  });

  group('evasion folds onto the right term', () {
    const Map<String, String> cases = <String, String>{
      r'$h!t': 'shit',
      '4r5e': 'arse',
      '5h1t': 'shit',
      r'a$$hole': 'asshole',
      'a-s-s': 'ass',
      '@rse': 'arse',
      'f u c k': 'fuck',
      'phuck': 'phuck',
      'b!tch': 'bitch',
      'c0ck': 'cock',
      'd1ck': 'dick',
      r'pu$$y': 'pussy',
      'p.u.s.s.y': 'pussy',
      'fuuuuuuck': 'fuck',
      'SHIT': 'shit',
      'Ｓｈｉｔ': 'shit',
    };

    cases.forEach((String text, String term) {
      test(text, () {
        final List<VulgarityMatch> hits = filter.scan(text);
        expect(hits, isNotEmpty, reason: "'$text' was not detected");
        expect(hits.first.term.text, term);
      });
    });
  });
}
