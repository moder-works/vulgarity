import 'package:test/test.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

VulgarityOptions _readOptions(Map<String, dynamic> c) {
  final Object? raw = c['options'];
  if (raw is! Map<String, dynamic>) {
    return VulgarityOptions();
  }

  Set<VulgarityCategory>? categories;
  final Object? names = raw['categories'];
  if (names is List) {
    categories = names
        .map((dynamic n) => VulgarityCategory.values.firstWhere(
            (VulgarityCategory v) => v.name == (n as String).toLowerCase()))
        .toSet();
  }

  return VulgarityOptions(
    minSeverity: raw['minSeverity'] as int? ?? 1,
    categories: categories,
    maskChar: raw['maskChar'] as String? ?? '*',
    maskToken: raw['maskToken'] as String?,
    repeatTolerance: raw['repeatTolerance'] as bool? ?? true,
    collapseContained: raw['collapseContained'] as bool? ?? true,
    scoreMode: raw['scoreMode'] == 'Max' ? ScoreMode.max : ScoreMode.total,
  );
}

void main() {
  final Map<String, dynamic> contract = readJson('vectors.json');

  test('vectors target this profile', () {
    expect(VulgarityFilter.profile, contract['profile']);
  });

  group('matches the contract', () {
    for (final dynamic entry in contract['cases'] as List<dynamic>) {
      final Map<String, dynamic> c = entry as Map<String, dynamic>;

      test(c['name'] as String, () {
        final String text = c['text'] as String;
        final VulgarityFilter filter =
            VulgarityFilter.createDefault(_readOptions(c));

        expect(filter.detect(text), c['detect'], reason: 'detect');
        expect(filter.score(text), c['score'], reason: 'score');
        expect(filter.filter(text), c['filtered'], reason: 'filter');

        final List<dynamic> expected = c['matches'] as List<dynamic>;
        final List<VulgarityMatch> actual = filter.scan(text);
        expect(actual.length, expected.length, reason: 'match count');

        for (int i = 0; i < expected.length; i++) {
          final Map<String, dynamic> m = expected[i] as Map<String, dynamic>;
          expect(actual[i].start, m['start'], reason: 'match $i start');
          expect(actual[i].end, m['end'], reason: 'match $i end');
          expect(actual[i].text, m['term'], reason: 'match $i term');
          expect(actual[i].categoryName, m['cat'], reason: 'match $i category');
          expect(actual[i].severity, m['sev'], reason: 'match $i severity');
        }
      });
    }
  });
}
