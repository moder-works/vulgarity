import 'package:test/test.dart';
import 'package:vulgarity/languages.dart';
import 'package:vulgarity/src/pack_reader.dart';
import 'package:vulgarity/src/pack_text.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

void main() {
  test('English is the default', () {
    expect(kAvailableLanguages.first, 'en');
    expect(kAvailableLanguages.length, 15);
    expect(kLanguageSeeds.length, 14);
    expect(kLanguageSeeds.containsKey('en'), isFalse,
        reason: 'useDefaultSeed already loads English');
  });

  /// Reads one bundled pack straight out of the compiled constant.
  List<VulgarityTerm> bundled(String text) {
    final List<VulgarityTerm> terms = <VulgarityTerm>[];
    loadPack(
        tryReadPackText(text)!, VulgarityFilter.profile, terms, <String>[]);
    return terms;
  }

  group('each pack', () {
    kLanguageSeeds.forEach((String code, String text) {
      test('$code targets this profile', () {
        // loadPack refuses a pack built for another profile, so a load that
        // returns terms is itself the profile check.
        expect(bundled(text), isNotEmpty);

        expect(
          () => loadPack(
              tryReadPackText(text)!, 'fold-v0', <VulgarityTerm>[], <String>[]),
          throwsFormatException,
        );
      });

      test('$code carries no readable term', () {
        // The whole point of the pack. A term must not survive in the source
        // this constant compiles from.
        for (final VulgarityTerm term in bundled(text)) {
          if (term.text.length >= 4) {
            expect(text.contains(term.text), isFalse,
                reason: "the $code pack still holds '${term.text}'");
          }
        }
      });

      test('$code is marked unvetted', () {
        // "vetted" is metadata about the source list, not something the matcher
        // reads, so a pack does not carry it. Check the authored JSON, which is
        // where that claim lives.
        final Map<String, dynamic> doc = readJson('seed.$code.json');
        expect(doc['lang'], code);
        expect(doc['vetted'], isFalse,
            reason: "Pack '$code' claims it is vetted. Nobody vetted it.");
      });

      test('$code finds every term it carries', () {
        final VulgarityFilter filter =
            (VulgarityFilterBuilder()..addSeed(text)).build();
        final List<String> missed = <String>[];

        for (final VulgarityTerm term in bundled(text)) {
          if (!filter.detect(term.text)) {
            missed.add(term.text);
          }
        }

        expect(missed, isEmpty,
            reason:
                "Pack '$code' holds ${missed.length} terms it cannot find.");
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
