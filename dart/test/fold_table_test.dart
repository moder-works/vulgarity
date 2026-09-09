import 'package:test/test.dart';
import 'package:vulgarity/src/normalization/fold_table.g.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

void main() {
  final Map<String, dynamic> contract = readJson('fold-v1.json');

  group('the compiled fold table matches data/fold-v1.json', () {
    test('profile', () {
      expect(kFoldProfile, contract['profile']);
      expect(VulgarityFilter.profile, kFoldProfile);
    });

    test('soft folds', () {
      final Map<String, dynamic> map =
          contract['foldSoft'] as Map<String, dynamic>;
      expect(kFoldSoft.length, map.length);
      map.forEach((String key, dynamic value) {
        expect(kFoldSoft[key.runes.first], value, reason: 'soft fold $key');
      });
    });

    test('hard folds', () {
      final Map<String, dynamic> map =
          contract['foldHard'] as Map<String, dynamic>;
      expect(kFoldHard.length, map.length);
      map.forEach((String key, dynamic value) {
        expect(kFoldHard[key.runes.first], value, reason: 'hard fold $key');
      });
    });

    test('ranges', () {
      final List<dynamic> ranges = contract['ranges'] as List<dynamic>;
      expect(kFoldRanges.length, ranges.length);
      for (int i = 0; i < ranges.length; i++) {
        final Map<String, dynamic> r = ranges[i] as Map<String, dynamic>;
        expect(kFoldRanges[i][0], r['from']);
        expect(kFoldRanges[i][1], r['to']);
        expect(kFoldRanges[i][2], r['base']);
      }
    });

    for (final String key in <String>['dropBreak', 'dropSilent']) {
      test(key, () {
        final List<dynamic> ranges = contract[key] as List<dynamic>;
        final List<List<int>> compiled =
            key == 'dropBreak' ? kDropBreak : kDropSilent;
        expect(compiled.length, ranges.length);
        for (int i = 0; i < ranges.length; i++) {
          final List<dynamic> r = ranges[i] as List<dynamic>;
          expect(compiled[i][0], r[0]);
          expect(compiled[i][1], r[1]);
        }
      });
    }
  });

  test('no fold map holds an ASCII letter', () {
    // The normalizer reads ASCII letters before it reads either fold map.
    // That shortcut is only safe while this holds.
    for (final int cp in <int>[...kFoldSoft.keys, ...kFoldHard.keys]) {
      final bool isLetter =
          (cp >= 0x61 && cp <= 0x7A) || (cp >= 0x41 && cp <= 0x5A);
      expect(isLetter, isFalse,
          reason: 'a fold map holds ${String.fromCharCode(cp)}');
    }
  });

  test('every fold produces plain ASCII letters', () {
    for (final String value in <String>[
      ...kFoldSoft.values,
      ...kFoldHard.values
    ]) {
      expect(value, isNotEmpty);
      for (final int c in value.codeUnits) {
        expect(c >= 0x61 && c <= 0x7A, isTrue,
            reason: "fold produced '$value'");
      }
    }
  });
}
