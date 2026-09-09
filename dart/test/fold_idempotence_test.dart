import 'package:test/test.dart';
import 'package:vulgarity/src/normalization/text_normalizer.dart';
import 'package:vulgarity/vulgarity.dart';

/// Folding has to reach a fixed point in one pass.
///
/// Terms are stored folded and text is folded at scan time, so a character that
/// keeps changing would make its own term unreachable. Fullwidth digits used to
/// fold onto ASCII digits, which then folded again onto soft letters.
void main() {
  test('folding every code point is idempotent', () {
    final List<String> offenders = <String>[];

    for (int cp = 0; cp <= 0x1FFFF; cp++) {
      if (cp >= 0xD800 && cp <= 0xDFFF) {
        continue; // A lone surrogate is not a character.
      }

      final String source = String.fromCharCode(cp);
      final String once = TextNormalizer.foldToString(source);
      final String twice = TextNormalizer.foldToString(once);

      if (once != twice) {
        offenders.add('U+${cp.toRadixString(16).toUpperCase().padLeft(4, '0')}'
            " folds to '$once', then to '$twice'");
      }
    }

    expect(offenders, isEmpty,
        reason: '${offenders.length} code points fold twice: '
            '${offenders.take(10).join('; ')}');
  });

  test('a fullwidth digit folds like its ASCII digit', () {
    const Map<String, String> cases = <String, String>{
      '０': 'o',
      '１': 'i',
      '３': 'e',
      '４': 'a',
      '５': 's',
      '７': 't',
      '２': '2',
      '６': '6',
      '８': '8',
      '９': '9',
    };

    cases.forEach((String wide, String expected) {
      expect(TextNormalizer.foldToString(wide), expected, reason: wide);
    });
  });

  test('a preset that folds two terms together builds', () {
    // "sh０t" and "shot" both fold to "shot". The builder merges them; it used
    // to throw, because the first one folded to "sh0t" and then to "shot".
    final VulgarityFilter filter = VulgarityFilter.fromPreset(
      '{"languages":["en"],"entries":['
      '{"t":"sh０t","sev":2},{"t":"shot","sev":2}]}',
    );
    expect(filter.detect('what a shot'), isTrue);
    expect(filter.detect('what a ｓｈ０ｔ'), isTrue);
  });
}
