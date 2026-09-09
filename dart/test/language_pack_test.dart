import 'dart:convert';

import 'package:test/test.dart';
import 'package:vulgarity/languages.dart';
import 'package:vulgarity/vulgarity.dart';

void main() {
  test('English is the default', () {
    expect(kAvailableLanguages.first, 'en');
    expect(kAvailableLanguages.length, 15);
    expect(kLanguageSeeds.length, 14);
    expect(kLanguageSeeds.containsKey('en'), isFalse,
        reason: 'useDefaultSeed already loads English');
  });

  group('each pack', () {
    kLanguageSeeds.forEach((String code, String json) {
      final Map<String, dynamic> doc =
          jsonDecode(json) as Map<String, dynamic>;

      test('$code targets this profile', () {
        expect(doc['profile'], VulgarityFilter.profile);
        expect(doc['schema'], 1);
        expect(doc['lang'], code);
        expect((doc['entries'] as List<dynamic>).isNotEmpty, isTrue);
      });

      test('$code is marked unvetted', () {
        expect(doc['vetted'], isFalse,
            reason: "Pack '$code' claims it is vetted. Nobody vetted it.");
      });

      test('$code finds every term it carries', () {
        final VulgarityFilter filter =
            (VulgarityFilterBuilder()..addSeed(json)).build();
        final List<String> missed = <String>[];

        for (final dynamic entry in doc['entries'] as List<dynamic>) {
          final String term = (entry as Map<String, dynamic>)['t'] as String;
          if (!filter.detect(term)) {
            missed.add(term);
          }
        }

        expect(missed, isEmpty,
            reason: "Pack '$code' holds ${missed.length} terms it cannot find.");
      });
    });
  });

  test('an optional pack adds terms to English', () {
    final VulgarityFilter english = VulgarityFilter.createDefault();
    final VulgarityFilter both = (VulgarityFilterBuilder()
          ..useDefaultSeed()
          ..addSeed(languageSeed('es')))
        .build();

    expect(both.termCount, greaterThan(english.termCount));

    // English still works after the pack loads.
    expect(both.detect('what the fuck'), isTrue);
    expect(both.detect('Have a nice day.'), isFalse);
  });

  test('an unknown pack is refused', () {
    expect(() => languageSeed('xx'), throwsArgumentError);
  });
}
