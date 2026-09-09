import 'package:test/test.dart';
import 'package:vulgarity/src/normalization/text_normalizer.dart';
import 'package:vulgarity/src/trie/aho_corasick.dart';

List<RawHit> scan(AhoCorasick trie, String text) {
  final List<RawHit> hits = <RawHit>[];
  trie.scan(TextNormalizer.normalize(text), hits);
  return hits;
}

void main() {
  test('finds every pattern in one pass', () {
    final AhoCorasick trie = AhoCorasick();
    final int he = trie.add(TextNormalizer.foldTerm('he'));
    final int she = trie.add(TextNormalizer.foldTerm('she'));
    final int his = trie.add(TextNormalizer.foldTerm('his'));
    final int hers = trie.add(TextNormalizer.foldTerm('hers'));
    trie.build();

    final List<RawHit> hits = scan(trie, 'ushers');

    // "ushers" holds "she" at 1..3, "he" at 2..3 and "hers" at 2..5.
    expect(hits.any((RawHit h) => h.patternId == she && h.end == 3), isTrue);
    expect(hits.any((RawHit h) => h.patternId == he && h.end == 3), isTrue);
    expect(hits.any((RawHit h) => h.patternId == hers && h.end == 5), isTrue);
    expect(hits.any((RawHit h) => h.patternId == his), isFalse);
  });

  test('reports a pattern that is also a suffix', () {
    final AhoCorasick trie = AhoCorasick();
    final int shit = trie.add(TextNormalizer.foldTerm('shit'));
    final int bullshit = trie.add(TextNormalizer.foldTerm('bullshit'));
    trie.build();

    final List<RawHit> hits = scan(trie, 'bullshit');
    expect(hits.any((RawHit h) => h.patternId == shit), isTrue);
    expect(hits.any((RawHit h) => h.patternId == bullshit), isTrue);
  });

  test('gives a duplicate pattern the same id', () {
    final AhoCorasick trie = AhoCorasick();
    expect(trie.add(<int>[97, 98, 99]), trie.add(<int>[97, 98, 99]));
    expect(trie.patternCount, 1);
  });

  test('reports the pattern length', () {
    final AhoCorasick trie = AhoCorasick();
    final int id = trie.add(TextNormalizer.foldTerm('hello'));
    trie.build();
    expect(trie.lengthOf(id), 5);
  });

  test('refuses an empty pattern', () {
    expect(() => AhoCorasick().add(<int>[]), throwsArgumentError);
  });

  test('refuses a pattern after build', () {
    final AhoCorasick trie = AhoCorasick()..add(<int>[97]);
    trie.build();
    expect(() => trie.add(<int>[98]), throwsStateError);
  });

  test('refuses a scan before build', () {
    final AhoCorasick trie = AhoCorasick()..add(<int>[97]);
    expect(() => scan(trie, 'a'), throwsStateError);
  });

  test('an empty trie finds nothing', () {
    final AhoCorasick trie = AhoCorasick()..build();
    expect(scan(trie, 'anything'), isEmpty);
  });
}
