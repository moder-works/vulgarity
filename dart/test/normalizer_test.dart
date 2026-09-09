import 'package:test/test.dart';
import 'package:vulgarity/src/normalization/normalized_text.dart';
import 'package:vulgarity/src/normalization/text_normalizer.dart';

import 'test_data.dart';

String _asString(NormalizedText n) => String.fromCharCodes(n.chars);

String _flags(List<bool> values) =>
    values.map((bool v) => v ? '1' : '0').join();

String _numbers(List<int> values) => values.join(',');

void main() {
  final Map<String, dynamic> contract = readJson('fold-vectors.json');

  group('folds to the contract', () {
    for (final dynamic entry in contract['cases'] as List<dynamic>) {
      final Map<String, dynamic> c = entry as Map<String, dynamic>;
      final String text = c['text'] as String;

      test(text.isEmpty ? '(empty)' : text, () {
        final NormalizedText n = TextNormalizer.normalize(text);

        expect(_asString(n), c['chars']);
        expect(_flags(n.hard), c['hard']);
        expect(_flags(n.gap), c['gap']);
        expect(_numbers(n.srcStart), c['srcStart']);
        expect(_numbers(n.srcEnd), c['srcEnd']);

        final NormalizedText? s = TextNormalizer.squeeze(n);
        if (c['squeezed'] == null) {
          expect(s, isNull);
        } else {
          expect(s, isNotNull);
          expect(_asString(s!), c['squeezed']);
        }
      });
    }
  });

  test('offsets always point inside the original', () {
    const String text = 'a🙂b.c‍d ẞ é';
    final NormalizedText n = TextNormalizer.normalize(text);
    for (int i = 0; i < n.length; i++) {
      expect(n.srcStart[i], inInclusiveRange(0, text.length - 1));
      expect(n.srcEnd[i], inInclusiveRange(1, text.length));
      expect(n.srcStart[i] < n.srcEnd[i], isTrue);
      if (i > 0) {
        expect(n.srcStart[i] >= n.srcStart[i - 1], isTrue,
            reason: 'offsets must not go backwards');
      }
    }
  });

  test('squeeze keeps the first offset and extends the last', () {
    final NormalizedText n = TextNormalizer.normalize('fuuuck');
    final NormalizedText s = TextNormalizer.squeeze(n)!;

    expect(_asString(s), 'fuck');
    expect(s.srcStart[1], 1); // the run starts at the first 'u'
    expect(s.srcEnd[1], 4); // and ends after the last 'u'
  });

  test('folding a term is idempotent', () {
    // Seed terms arrive already folded. Folding again must not change them.
    final Map<String, dynamic> seed = readJson('seed.json');
    for (final dynamic entry in seed['entries'] as List<dynamic>) {
      final String term = (entry as Map<String, dynamic>)['t'] as String;
      expect(TextNormalizer.foldToString(term), term);
    }
  });
}
