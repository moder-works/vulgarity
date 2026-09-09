import 'package:test/test.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

VulgarityFilter buildWithoutAllowlist() {
  final Map<String, dynamic> seed = readJson('seed.json');
  final VulgarityFilterBuilder builder = VulgarityFilterBuilder();
  for (final dynamic entry in seed['entries'] as List<dynamic>) {
    final Map<String, dynamic> e = entry as Map<String, dynamic>;
    builder.addTerm(
        e['t'] as String, e['cat'] as String, e['sev'] as int, e['w'] == true);
  }
  return builder.build();
}

void main() {
  final Map<String, dynamic> seed = readJson('seed.json');

  test('seed targets this profile', () {
    expect(seed['profile'], VulgarityFilter.profile);
    expect(seed['schema'], 1);
  });

  test('every term is plain and unique', () {
    final Set<String> seen = <String>{};
    for (final dynamic entry in seed['entries'] as List<dynamic>) {
      final Map<String, dynamic> e = entry as Map<String, dynamic>;
      final String term = e['t'] as String;

      expect(term, isNotEmpty);
      expect(seen.add(term), isTrue, reason: 'duplicate term: $term');
      expect(RegExp(r'^[a-z0-9]+$').hasMatch(term), isTrue,
          reason: "term '$term' is not folded to fold-v1");
      expect(e['sev'] as int, inInclusiveRange(1, 5));
      expect(e['cat'] as String, isNotEmpty);
    }
  });

  test('loading the seed refuses the wrong profile', () {
    expect(
      () => VulgarityFilter.fromSeed(
          '{"schema":1,"profile":"fold-v9","entries":[{"t":"x","cat":"a","sev":1}]}'),
      throwsFormatException,
    );
  });

  test('loading the seed refuses the wrong schema', () {
    expect(
      () => VulgarityFilter.fromSeed(
          '{"schema":99,"profile":"fold-v1","entries":[]}'),
      throwsFormatException,
    );
  });

  test('every allowlist entry is needed', () {
    // An allowlist entry the matcher does not need is worse than useless.
    // "therapist" sat in the list once, and it suppressed the correct match on
    // "the rapist".
    final VulgarityFilter bare = buildWithoutAllowlist();
    for (final dynamic word in seed['allow'] as List<dynamic>) {
      expect(bare.detect(word as String), isTrue,
          reason: "Allowlist entry '$word' is not needed. The matcher never "
              'flags it, so the entry only risks suppressing a real match.');
    }
  });

  test('allowlist words stay clean', () {
    final VulgarityFilter filter = VulgarityFilter.createDefault();
    for (final dynamic word in seed['allow'] as List<dynamic>) {
      expect(filter.detect(word as String), isFalse,
          reason: "Allowlist entry '$word' is still flagged.");
    }
  });

  test('the boundary test separates the phrase from the word', () {
    final VulgarityFilter filter = VulgarityFilter.createDefault();
    // The gap between the two words is what makes this pair work.
    expect(filter.detect('the rapist was caught'), isTrue);
    expect(filter.detect('my therapist is great'), isFalse);
  });
}
