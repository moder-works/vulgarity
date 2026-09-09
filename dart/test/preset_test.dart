import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';
import 'package:vulgarity/languages.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

String readPreset(String name) =>
    File('${dataDirectory.path}/presets/$name.json').readAsStringSync();

VulgarityFilter filterFor(String preset) =>
    VulgarityFilter.fromPreset(VulgarityPreset.parse(readPreset(preset)),
        languageResolver: languageSeed);

void main() {
  final Map<String, dynamic> contract = readJson('preset-vectors.json');

  test('preset vectors target this profile', () {
    expect(VulgarityFilter.profile, contract['profile']);
  });

  group('matches the contract', () {
    for (final dynamic entry in contract['cases'] as List<dynamic>) {
      final Map<String, dynamic> c = entry as Map<String, dynamic>;
      final String text = c['text'] as String;

      test('${c['preset']}: $text', () {
        final VulgarityFilter filter = filterFor(c['preset'] as String);

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
          expect(actual[i].term.text, m['term'], reason: 'match $i term');
          expect(actual[i].severity, m['sev'], reason: 'match $i severity');
        }
      });
    }
  });

  test('the default preset equals createDefault', () {
    expect(filterFor('default').termCount,
        VulgarityFilter.createDefault().termCount);
  });

  test('a preset carries its policy', () {
    final VulgarityPreset preset =
        VulgarityPreset.parse(readPreset('hate-only'));
    expect(preset.name, 'hate-only');
    expect(preset.options.minSeverity, 4);
    expect(preset.options.maskChar, '#');
    expect(preset.options.categories, contains(VulgarityCategory.hate));
    expect(preset.options.categories,
        isNot(contains(VulgarityCategory.profanity)));
  });

  test('remove drops a term from the base list', () {
    expect(VulgarityFilter.createDefault().detect('oh damn'), isTrue);
    expect(filterFor('brand').detect('oh damn'), isFalse);
  });

  test('removing an absent term is not an error', () {
    // A remote policy must keep working against an older term list.
    final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
      ..useDefaultSeed();
    final int before = builder.termCount;
    builder.removeTerm('thistermwasneverhere');
    expect(builder.termCount, before);
  });

  test('your own options beat the preset', () {
    final VulgarityFilter loose = (VulgarityFilterBuilder()
          ..addPreset(VulgarityPreset.parse(readPreset('hate-only')),
              languageResolver: languageSeed))
        .build(const VulgarityOptions(minSeverity: 1));
    expect(loose.detect('oh damn'), isTrue);
  });

  test('a preset round trips through JSON', () {
    final VulgarityPreset original = VulgarityPreset.parse(readPreset('brand'));
    final VulgarityPreset again =
        VulgarityPreset.parse(jsonEncode(original.toJson()));

    expect(again.name, original.name);
    expect(again.languages, original.languages);
    expect(again.entries.length, original.entries.length);
    expect(again.remove, original.remove);
    expect(again.options.minSeverity, original.options.minSeverity);
    expect(again.options.maskChar, original.options.maskChar);
  });

  test('options round trip through JSON', () {
    final VulgarityOptions original = VulgarityOptions(
      minSeverity: 3,
      maskToken: '[x]',
      scoreMode: ScoreMode.max,
      repeatTolerance: false,
      collapseContained: false,
      categories: <VulgarityCategory>{
        VulgarityCategory.hate,
        VulgarityCategory.drug,
      },
    );
    final VulgarityOptions again = VulgarityOptions.fromJson(original.toJson());

    expect(again.minSeverity, original.minSeverity);
    expect(again.maskToken, original.maskToken);
    expect(again.scoreMode, original.scoreMode);
    expect(again.repeatTolerance, original.repeatTolerance);
    expect(again.collapseContained, original.collapseContained);
    expect(again.categories, original.categories);
  });

  // A preset from the network is untrusted input.
  group('a malformed preset is refused', () {
    const Map<String, String> bad = <String, String>{
      'not json at all': 'not valid JSON',
      '[]': 'must be a JSON object',
      '{"schema":99}': 'preset schema',
      '{"profile":"fold-v9"}': 'fold profile',
      '{"languages":"en"}': 'must be an array',
      '{"options":{"scoreMode":"sideways"}}': 'scoreMode',
      '{"options":{"categories":["nonsense"]}}': 'does not know',
      '{"options":{"maskChar":"toolong"}}': 'exactly one character',
      // A field out of range is as malformed as a field of the wrong type. It
      // used to leak a RangeError through the FormatException contract.
      '{"options":{"minSeverity":9}}': 'minSeverity',
      '{"entries":[{"t":"x","sev":77}]}': 'Severity must be 1 to 5',
      '{"entries":[{"cat":"hate"}]}': "must hold a non-empty 't' term",
      '{"entries":"nope"}': 'must be an array',
      '{"allow":[5]}': 'must hold strings only',
    };

    bad.forEach((String json, String message) {
      test(json, () {
        expect(
          () => VulgarityPreset.parse(json),
          throwsA(isA<FormatException>().having(
              (FormatException e) => e.toString(),
              'message',
              contains(message))),
        );
      });
    });
  });

  // The shared malformed-preset contract. Both ports read this file, so a
  // document one port refuses and the other accepts fails here.
  group('the shared malformed preset vectors are refused', () {
    final Map<String, dynamic> errors = readJson('preset-error-vectors.json');

    for (final dynamic entry in errors['cases'] as List<dynamic>) {
      final Map<String, dynamic> c = entry as Map<String, dynamic>;
      final String document = c['preset'] as String;
      final String field = c['field'] as String;
      final String? mustHold = c['contains'] as String?;
      final String? mustNotHold = c['absent'] as String?;
      final bool build = c['stage'] == 'build';

      test(c['name'] as String, () {
        Object? thrown;
        try {
          final VulgarityPreset preset = VulgarityPreset.parse(document);
          if (build) {
            VulgarityFilter.fromPreset(preset, languageResolver: languageSeed);
          }
        } catch (error) {
          thrown = error;
        }

        expect(thrown, isA<FormatException>(),
            reason: 'expected a FormatException, got $thrown');

        final String message = thrown.toString();
        expect(message.toLowerCase(), contains(field.toLowerCase()),
            reason: "the message must name '$field': $message");
        if (mustHold != null) {
          expect(message, contains(mustHold), reason: message);
        }
        if (mustNotHold != null) {
          expect(message, isNot(contains(mustNotHold)), reason: message);
        }
      });
    }
  });

  test('an unknown term category is tolerated as other', () {
    // A term category stays lenient, so an older client keeps working when a
    // server adds a category.
    final VulgarityPreset preset = VulgarityPreset.parse('{"schema":1,'
        '"profile":"fold-v1","languages":["en"],'
        '"entries":[{"t":"blorp","cat":"brandnew","sev":3}]}');

    expect(preset.entries.first.category, VulgarityCategory.other);
    expect(preset.entries.first.categoryName, 'brandnew');
  });

  test('an empty preset still needs terms', () {
    expect(
        () => VulgarityFilter.fromPreset(
            VulgarityPreset.parse('{"schema":1,"profile":"fold-v1"}')),
        throwsStateError);
  });

  test('a non-English language needs a resolver', () {
    final VulgarityPreset preset = VulgarityPreset.parse(
        '{"schema":1,"profile":"fold-v1","languages":["en","es"]}');
    expect(() => VulgarityFilter.fromPreset(preset), throwsArgumentError);

    final VulgarityFilter ok =
        VulgarityFilter.fromPreset(preset, languageResolver: languageSeed);
    expect(ok.detect('eres un cabron'), isTrue);
  });

  test('English alone needs no resolver', () {
    final VulgarityFilter filter = VulgarityFilter.fromPreset(
        VulgarityPreset.parse(
            '{"schema":1,"profile":"fold-v1","languages":["en"]}'));
    expect(filter.detect('what the fuck'), isTrue);
  });
}
