import 'dart:convert';

import 'package:test/test.dart';
import 'package:vulgarity/languages.dart';
import 'package:vulgarity/src/pack_reader.dart';
import 'package:vulgarity/src/pack_text.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

/// Both ports must accept the same base64 pack text.
///
/// The two runtimes disagree, and in opposite directions. This port's
/// `base64.decode` refuses every blank character but accepts the URL-safe
/// alphabet. .NET's `Convert.FromBase64String` does the reverse. So
/// `base64 < x.vpk`, which wraps at 76 columns, would load there and fail here.
/// `pack_text.dart` normalises first.
/// `dotnet/tests/Vulgarity.Tests/PackTextTests.cs` is the mirror of this file.
void main() {
  final String packText = languageSeed('en');

  void expectAccepted(String text, String shape) {
    final List<int>? pack = tryReadPackText(text);
    expect(pack, isNotNull, reason: 'this port refused $shape');

    final List<VulgarityTerm> terms = <VulgarityTerm>[];
    loadPack(pack!, VulgarityFilter.profile, terms, <String>[]);
    expect(terms, isNotEmpty);
  }

  test('plain base64 is accepted', () {
    expectAccepted(packText, 'plain base64');
  });

  test('line-wrapped base64 is accepted', () {
    // This is what `base64 < pack.vpk` produces.
    final StringBuffer wrapped = StringBuffer();
    for (int i = 0; i < packText.length; i += 76) {
      final int end = i + 76 < packText.length ? i + 76 : packText.length;
      wrapped
        ..write(packText.substring(i, end))
        ..write('\n');
    }
    expectAccepted(wrapped.toString(), 'base64 wrapped at 76 columns');
  });

  test('URL-safe base64 is accepted', () {
    // This is what a caller who put the pack in a URL produces.
    expectAccepted(
      packText.replaceAll('+', '-').replaceAll('/', '_'),
      'the URL-safe base64 alphabet',
    );
  });

  test('surrounding blank space is accepted', () {
    expectAccepted('\n\t $packText \r\n', 'base64 with blank space around it');
  });

  test('text that is not a pack is refused', () {
    expect(tryReadPackText(''), isNull);
    expect(tryReadPackText('   '), isNull);
    expect(tryReadPackText('{"entries":[]}'), isNull);
    expect(tryReadPackText('not base64 at all !!!'), isNull);
    expect(tryReadPackText(base64.encode(utf8.encode('this is not a pack'))),
        isNull);
  });

  test('a JSON document still takes the JSON path', () {
    // The documented remote path must not change.
    final VulgarityFilter filter =
        (VulgarityFilterBuilder()..addSeed(readData('seed.json'))).build();
    expect(filter.termCount, 526);
  });

  test('something that is neither is refused clearly', () {
    expect(
      () => VulgarityFilterBuilder().addSeed('hello'),
      throwsA(isA<FormatException>().having(
          (FormatException e) => e.message, 'message', contains('pack'))),
    );
  });
}
