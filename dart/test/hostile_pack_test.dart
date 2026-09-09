import 'dart:convert';

import 'package:test/test.dart';
import 'package:vulgarity/src/pack_reader.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

/// Checks the pack reader against data/testdata/hostile-packs.json.
///
/// The C# reader and tool/packlib.py read the same file and assert the same
/// outcomes, so a pack one reader accepts and another refuses fails in three
/// places at once. tool/gen_hostile_packs.py writes the file.
void main() {
  final Map<String, dynamic> fixtures = readFixture('hostile-packs.json');
  final String profile = fixtures['profile'] as String;

  test('the hostile packs target this profile', () {
    expect(VulgarityFilter.profile, profile);
  });

  group('the pack reader agrees with the shared fixtures', () {
    for (final dynamic entry in fixtures['cases'] as List<dynamic>) {
      final Map<String, dynamic> c = entry as Map<String, dynamic>;
      final String name = c['name'] as String;
      final List<int> pack = base64.decode(c['pack'] as String);

      test(name, () {
        final List<VulgarityTerm> terms = <VulgarityTerm>[];
        final List<String> allow = <String>[];

        if (c['expect'] == 'error') {
          expect(
            () => loadPack(pack, profile, terms, allow),
            throwsA(isA<FormatException>()),
            reason: '$name must be refused',
          );
          return;
        }

        loadPack(pack, profile, terms, allow);
        expect(terms.length, c['terms'], reason: '$name term count');
      });
    }
  });

  test('a pack that loads still builds a usable filter', () {
    // The valid fixture is tiny, so this also proves a pack needs nothing from
    // the bundled list to stand on its own.
    final Map<String, dynamic> valid = (fixtures['cases'] as List<dynamic>)
        .cast<Map<String, dynamic>>()
        .firstWhere(
            (Map<String, dynamic> c) => c['name'] == 'a valid tiny pack');

    final VulgarityFilter filter = (VulgarityFilterBuilder()
          ..addSeedBytes(base64.decode(valid['pack'] as String)))
        .build();

    expect(filter.termCount, valid['terms']);
    expect(filter.detect('blorp'), isTrue);
    expect(filter.detect('nothing here'), isFalse);
  });
}
