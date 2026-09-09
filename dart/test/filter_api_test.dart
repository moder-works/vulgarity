import 'package:test/test.dart';
import 'package:vulgarity/vulgarity.dart';

void main() {
  final VulgarityFilter filter = VulgarityFilter.createDefault();

  group('empty input is handled', () {
    for (final String? text in <String?>[null, '']) {
      test(text == null ? 'null' : 'empty string', () {
        expect(filter.detect(text), isFalse);
        expect(filter.scan(text), isEmpty);
        expect(filter.score(text), 0);
        expect(filter.filter(text), text);
      });
    }
  });

  test('offsets point into the original text', () {
    const String text = 'café 🙂 f.u.c.k end';
    final List<VulgarityMatch> hits = filter.scan(text);
    expect(hits.length, 1);
    expect(hits.first.excerpt(text), 'f.u.c.k');
    expect(hits.first.length, hits.first.end - hits.first.start);
    expect(hits.first.text, 'fuck');
  });

  test('masking never changes the length with a mask character', () {
    const String text = 'oh fuck that shit';
    final String masked = filter.filter(text)!;
    expect(masked.length, text.length);
    expect(masked.contains('fuck'), isFalse);
    expect(masked.contains('shit'), isFalse);
  });

  test('mask token replaces the whole span', () {
    final VulgarityFilter f =
        filter.withOptions(VulgarityOptions(maskToken: '[x]'));
    expect(f.filter('oh fuck'), 'oh [x]');
  });

  test('score modes differ', () {
    const String text = 'fuck this shit';
    expect(
        filter.withOptions(VulgarityOptions()).score(text), 7);
    expect(
        filter.withOptions(VulgarityOptions(scoreMode: ScoreMode.max)).score(text), 4);
  });

  test('category filter restricts the result', () {
    final VulgarityFilter f = filter.withOptions(
        VulgarityOptions(categories: <VulgarityCategory>{VulgarityCategory.hate}));
    expect(f.detect('what the fuck'), isFalse);
    expect(f.detect('that faggot'), isTrue);
  });

  test('min severity removes clinical terms', () {
    final VulgarityFilter f =
        filter.withOptions(VulgarityOptions(minSeverity: 2));
    expect(f.detect('the penis and the vagina'), isFalse);
    expect(filter.detect('the penis and the vagina'), isTrue);
  });

  test('options are validated', () {
    expect(() => filter.withOptions(VulgarityOptions(minSeverity: 0)),
        throwsRangeError);
    expect(() => filter.withOptions(VulgarityOptions(maskToken: '')),
        throwsArgumentError);
    expect(() => filter.withOptions(VulgarityOptions(maskChar: '**')),
        throwsArgumentError);
  });

  test('a builder accepts custom terms and an allowlist', () {
    final VulgarityFilter f = (VulgarityFilterBuilder()
          ..addTerm('Blorp', 'profanity', 3, true)
          ..addAllow('blorpshire'))
        .build();

    expect(f.detect('that is blorp'), isTrue);
    expect(f.detect('BL0RP'), isTrue); // the builder folds the term
    expect(f.detect('I live in Blorpshire'), isFalse);
  });

  test('a builder needs at least one term', () {
    expect(() => VulgarityFilterBuilder().build(), throwsStateError);
  });

  test('a duplicate term keeps the worse rating', () {
    final VulgarityFilter f = (VulgarityFilterBuilder()
          ..addTerm('blorp', 'profanity', 2, true)
          ..addTerm('blorp', 'hate', 5, true))
        .build();

    expect(f.termCount, 1);
    final List<VulgarityMatch> hits = f.scan('blorp');
    expect(hits.length, 1);
    expect(hits.first.severity, 5);
    expect(hits.first.category, VulgarityCategory.hate);
  });

  test('matches arrive in order', () {
    final List<VulgarityMatch> hits =
        filter.scan('fuck this shit and that cunt');
    for (int i = 1; i < hits.length; i++) {
      expect(hits[i - 1].start <= hits[i].start, isTrue,
          reason: 'matches are out of order');
    }
    expect(hits.length, 3);
  });

  test('repeat tolerance can be turned off', () {
    final VulgarityFilter f =
        filter.withOptions(VulgarityOptions(repeatTolerance: false));
    expect(filter.detect('fuuuck'), isTrue);
    expect(f.detect('fuuuck'), isFalse);
    expect(f.detect('fuck'), isTrue);
  });

  test('the squeeze pass never widens a span stream A already found', () {
    // "this shit" squeezes to "thishit", which would report "s shit".
    const String text = 'fuck this shit';
    final List<VulgarityMatch> hits = filter.scan(text);
    expect(hits.length, 2);
    expect(hits[1].excerpt(text), 'shit');
  });

  test('long text stays fast', () {
    final String text = '${'a' * 50000} fuck ${'b' * 50000}';
    final Stopwatch watch = Stopwatch()..start();
    expect(filter.detect(text), isTrue);
    watch.stop();
    expect(watch.elapsedMilliseconds, lessThan(2000),
        reason: 'a 100 KB scan took ${watch.elapsedMilliseconds} ms');
  });
}
