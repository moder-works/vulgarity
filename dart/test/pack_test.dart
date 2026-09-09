import 'dart:convert';

import 'package:test/test.dart';
import 'package:vulgarity/src/pack_reader.dart';
import 'package:vulgarity/src/pack_text.dart';
import 'package:vulgarity/src/seed_loader.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

/// Checks that a pack carries exactly what its JSON seed carries.
///
/// The packs are what ships. The JSON stays the authored source. These two must
/// never drift, so every bundled list is read both ways and compared.
void main() {
  const List<String> codes = <String>[
    'en', 'ar', 'de', 'es', 'fa', 'fr', 'hi', 'it', //
    'ko', 'nl', 'pl', 'ru', 'th', 'vi', 'zh',
  ];

  String seedName(String code) =>
      code == 'en' ? 'seed.json' : 'seed.$code.json';
  String packName(String code) => 'packs/seed-$code.vpk';

  for (final String code in codes) {
    test('the $code pack reads back as its seed reads', () {
      final List<VulgarityTerm> fromJson = <VulgarityTerm>[];
      final List<String> allowFromJson = <String>[];
      loadSeed(readData(seedName(code)), VulgarityFilter.profile, fromJson,
          allowFromJson);

      final List<VulgarityTerm> fromPack = <VulgarityTerm>[];
      final List<String> allowFromPack = <String>[];
      loadPack(readDataBytes(packName(code)), VulgarityFilter.profile, fromPack,
          allowFromPack);

      expect(fromPack.length, fromJson.length);
      expect(allowFromPack, allowFromJson);

      for (int i = 0; i < fromJson.length; i++) {
        final VulgarityTerm want = fromJson[i];
        final VulgarityTerm got = fromPack[i];
        expect(got.text, want.text);
        expect(got.categoryName, want.categoryName);
        expect(got.category, want.category);
        expect(got.severity, want.severity);
        expect(got.requireBoundary, want.requireBoundary);
      }
    });
  }

  test('a pack carries no readable term', () {
    final List<VulgarityTerm> terms = <VulgarityTerm>[];
    final List<String> allow = <String>[];
    loadSeed(readData('seed.json'), VulgarityFilter.profile, terms, allow);

    final List<int> pack = readDataBytes(packName('en'));
    final String raw = String.fromCharCodes(pack);

    for (final VulgarityTerm term in terms) {
      if (term.text.length >= 4) {
        expect(raw.contains(term.text), isFalse,
            reason: "the pack still holds '${term.text}'");
      }
    }
  });

  test('something that is not a pack is refused', () {
    expect(looksLikePack(<int>[]), isFalse);
    expect(looksLikePack(utf8.encode('{}')), isFalse);
    expect(looksLikePack(utf8.encode('VPK')), isFalse);

    expect(
      () => loadPack(utf8.encode('{"entries":[]}'), VulgarityFilter.profile,
          <VulgarityTerm>[], <String>[]),
      throwsFormatException,
    );
  });

  test('a pack built for another profile is refused', () {
    expect(
      () => loadPack(readDataBytes(packName('en')), 'fold-v0',
          <VulgarityTerm>[], <String>[]),
      throwsFormatException,
    );
  });

  test('base64 pack text is recognised, and other text is not', () {
    final String text = base64.encode(readDataBytes(packName('en')));
    expect(tryReadPackText(text), isNotNull);
    expect(tryReadPackText('{"entries":[]}'), isNull);
    expect(tryReadPackText(''), isNull);
    expect(tryReadPackText(base64.encode(utf8.encode('not a pack at all'))),
        isNull);
  });

  test('addSeedBytes loads a pack straight from bytes', () {
    // The base64 path has its own tests. This one hands the builder raw pack
    // bytes, which is what an app hosting its own list does.
    final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
      ..addSeedBytes(readDataBytes('packs/seed-en.vpk'));

    expect(builder.termCount, seedEntryCount());
    expect(builder.hasTerm('damn'), isTrue);
    expect(builder.build().detect('what the fuck'), isTrue);
  });
}
