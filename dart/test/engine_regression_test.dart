import 'package:test/test.dart';
import 'package:vulgarity/vulgarity.dart';

/// Regression cover for the matching engine.
///
/// Each group names the defect it pins down. The shared vectors carry the same
/// cases, so both ports stay honest; these tests catch a break earlier and say
/// more about why.
void main() {
  final VulgarityFilter filter = VulgarityFilter.createDefault();

  String hits(String text) =>
      filter.scan(text).map((VulgarityMatch m) => m.text).join(',');

  group('a term must not span two innocent words', () {
    const List<String> clean = <String>[
      'wash it down',
      'push it harder',
      'polish items',
      'shop ornament',
      'this ape shot up',
      'a fresh it item',
    ];

    for (final String text in clean) {
      test(text, () {
        expect(filter.scan(text), isEmpty, reason: 'flagged ${hits(text)}');
      });
    }

    const List<String> flagged = <String>[
      'a55 hole',
      'f u c k',
      'ape-shit',
      'a-s-s',
      '.f uc k',
    ];

    for (final String text in flagged) {
      test('$text still matches', () {
        expect(filter.scan(text), isNotEmpty);
      });
    }

    test('the whole match is reported, not the tail', () {
      final List<VulgarityMatch> found = filter.scan('you fuck off');
      expect(found.length, 1);
      expect(found.first.start, 4);
      expect(found.first.end, 8);
    });
  });

  group('an invisible character is not a word boundary', () {
    const Map<String, String> cases = <String, String>{
      'cl­ass': 'soft hyphen',
      'cl​ass': 'zero width space',
      'gr⁠ass': 'word joiner',
      'cl‍ass': 'zero width joiner',
      'cl﻿ass': 'zero width no-break space',
    };

    cases.forEach((String text, String name) {
      test(name, () {
        expect(filter.scan(text), isEmpty, reason: 'flagged ${hits(text)}');
      });
    });

    test('a real separator still breaks a word', () {
      expect(filter.scan('cl ass'), isNotEmpty);
    });
  });

  group('repeat tolerance reaches a term with a doubled letter', () {
    const List<String> flagged = <String>[
      'asss',
      'assshole',
      'pusssy',
      'bolllock',
      'fuuuck',
      'what an assssshole',
    ];

    for (final String text in flagged) {
      test(text, () {
        expect(filter.scan(text), isNotEmpty);
      });
    }

    // A squeezed spelling must not stand in for a different real word. Text may
    // repeat a letter more often than the term does, never less.
    const List<String> clean = <String>[
      'was',
      'class',
      'bass line',
      'the heel of the boot',
      'conn the ship',
      'a contrafagotto solo',
    ];

    for (final String text in clean) {
      test('$text stays clean', () {
        expect(filter.scan(text), isEmpty, reason: 'flagged ${hits(text)}');
      });
    }

    test('an allowlisted word survives the squeeze pass', () {
      expect(filter.scan('I live in Scunnthorpe'), isEmpty);
    });
  });

  group('the squeeze pass never doubles up a plain match', () {
    test('bullshitter reports one term', () {
      final List<VulgarityMatch> found = filter.scan('bullshitter');
      expect(found.length, 1);
      expect(found.first.text, 'bullshit');
      expect(filter.score('bullshitter'), 3);
    });

    test('a squeezed candidate that overlaps nothing is kept', () {
      final List<VulgarityMatch> found = filter.scan('damn the fuuuck');
      expect(found.length, 2);
      expect(found.map((VulgarityMatch m) => m.text), <String>['damn', 'fuck']);
    });
  });

  group('a large text scans in linear time', () {
    // Both cases used to be quadratic: every match was compared against every
    // allow span, and every squeezed candidate against every plain match.
    // A megabyte took seconds.
    String repeat(String unit) {
      final StringBuffer buffer = StringBuffer();
      while (buffer.length < 1024 * 1024) {
        buffer.write(unit);
      }
      return buffer.toString();
    }

    int millisToScan(String text) {
      filter.scan(text.substring(0, 2048)); // warm the code up first
      final Stopwatch watch = Stopwatch()..start();
      filter.scan(text);
      watch.stop();
      return watch.elapsedMilliseconds;
    }

    test('a megabyte of allowlisted text', () {
      final int elapsed = millisToScan(repeat('scunthorpe '));
      expect(elapsed, lessThan(500), reason: '$elapsed ms');
    });

    test('a megabyte the squeeze pass hits on every word', () {
      final int elapsed = millisToScan(repeat('damn daamn '));
      expect(elapsed, lessThan(500), reason: '$elapsed ms');
    });
  });
}
