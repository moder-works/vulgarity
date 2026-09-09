import 'dart:convert';

import 'package:test/test.dart';
import 'package:vulgarity/languages.dart';
import 'package:vulgarity/src/pack_text.dart';
import 'package:vulgarity/src/seed_loader.dart';
import 'package:vulgarity/vulgarity.dart';

/// Guards the input-parsing contract: a malformed document throws
/// [FormatException], names the field, and never applies in part.
///
/// Every case here stood for a real defect. A document from the network is
/// untrusted, and each of these either leaked the wrong exception type, quietly
/// coerced a bad value into a good one, or left the builder holding half a
/// policy.
void main() {
  // --------------------------------------------------------------------
  // A malformed field is a FormatException that names the field.
  // --------------------------------------------------------------------
  group('a field of the wrong type is refused', () {
    // The value is the text the message must carry: the field name.
    const Map<String, String> bad = <String, String>{
      // Options. These used to be `as int?`, `as bool?` and `as String?`
      // casts, so a bad value threw TypeError instead.
      '{"options":{"minSeverity":"3"}}': 'minSeverity',
      '{"options":{"minSeverity":1.5}}': 'minSeverity',
      '{"options":{"minSeverity":true}}': 'minSeverity',
      '{"options":{"repeatTolerance":"yes"}}': 'repeatTolerance',
      '{"options":{"collapseContained":1}}': 'collapseContained',
      '{"options":{"maskToken":5}}': 'maskToken',
      '{"options":{"maskChar":5}}': 'maskChar',
      '{"options":{"scoreMode":7}}': 'scoreMode',
      '{"options":{"categories":"hate"}}': 'categories',
      // Out of range, which used to leak RangeError and ArgumentError.
      '{"options":{"minSeverity":9}}': 'minSeverity',
      '{"options":{"minSeverity":0}}': 'minSeverity',
      '{"options":{"maskToken":""}}': 'maskToken',
      // The document's own fields.
      '{"name":5}': 'name',
      '{"description":[1,2]}': 'description',
      '{"schema":"1"}': 'schema',
      '{"profile":5}': 'profile',
      // Entries.
      '{"entries":[{"t":"x","sev":"3"}]}': 'sev',
      '{"entries":[{"t":"x","cat":5}]}': 'cat',
      '{"entries":[{"t":"x","w":"yes"}]}': 'w',
    };

    bad.forEach((String json, String field) {
      test(json, () {
        expect(
          () => VulgarityPreset.parse(json),
          throwsA(isA<FormatException>().having(
              (FormatException e) => e.message, 'message', contains(field))),
        );
      });
    });

    test('a fractional schema is not a whole number', () {
      // 1.0 is not 1. On the Dart VM the two are different types, and the
      // check refuses the double. dart2js has one number type and cannot tell
      // them apart, which is a limit of that runtime, not of this rule.
      expect(() => VulgarityPreset.parse('{"schema":1.0}'),
          throwsA(isA<FormatException>()));
    });
  });

  group('the fields are checked in one fixed order', () {
    // Both ports must name the same field for the same bad document, or a
    // server cannot tell an operator what to fix. The order is minSeverity,
    // maskChar, maskToken, repeatTolerance, collapseContained, scoreMode,
    // categories.
    const Map<String, String> first = <String, String>{
      '{"options":{"minSeverity":9,"maskChar":"toolong"}}': 'minSeverity',
      '{"options":{"maskChar":"toolong","maskToken":""}}': 'maskChar',
      '{"options":{"maskToken":"","repeatTolerance":"yes"}}': 'maskToken',
      '{"options":{"repeatTolerance":"yes","collapseContained":"no"}}':
          'repeatTolerance',
      '{"options":{"collapseContained":"no","scoreMode":"sideways"}}':
          'collapseContained',
      '{"options":{"scoreMode":"sideways","categories":["nonsense"]}}':
          'scoreMode',
    };

    first.forEach((String json, String field) {
      test(json, () {
        expect(
          () => VulgarityPreset.parse(json),
          throwsA(isA<FormatException>().having(
              (FormatException e) => e.message, 'message', contains(field))),
        );
      });
    });
  });

  // --------------------------------------------------------------------
  // categories: an empty set and no set at all are opposite policies.
  // --------------------------------------------------------------------
  group('the category filter survives a round trip', () {
    test('an empty set stays an empty set', () {
      final VulgarityOptions original =
          VulgarityOptions(categories: <VulgarityCategory>{});
      final Map<String, dynamic> json = original.toJson();

      expect(json['categories'], isEmpty);
      expect(VulgarityOptions.fromJson(json).categories, isEmpty);
    });

    test('no filter leaves the key out entirely', () {
      final Map<String, dynamic> json = VulgarityOptions().toJson();

      expect(json.containsKey('categories'), isFalse);
      expect(VulgarityOptions.fromJson(json).categories, isNull);
    });

    test('the two mean opposite things', () {
      final VulgarityFilter none = VulgarityFilter.createDefault(
          VulgarityOptions(categories: <VulgarityCategory>{}));
      final VulgarityFilter all = VulgarityFilter.createDefault();

      expect(none.detect('oh damn'), isFalse,
          reason: 'an empty set matches no '
              'category');
      expect(all.detect('oh damn'), isTrue,
          reason: 'no filter matches every category');
    });

    test('a chosen set survives the round trip through a whole preset', () {
      final VulgarityOptions original = VulgarityOptions(
          categories: <VulgarityCategory>{VulgarityCategory.hate});
      final VulgarityPreset preset = VulgarityPreset(options: original);
      final VulgarityPreset again =
          VulgarityPreset.parse(jsonEncode(preset.toJson()));

      expect(again.options.categories, original.categories);
    });
  });

  // --------------------------------------------------------------------
  // addPreset applies everything or nothing.
  // --------------------------------------------------------------------
  group('a preset that fails leaves the builder untouched', () {
    test('a second language it cannot serve unloads the first', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder();

      expect(
        () => builder.addPreset('{"languages":["en","pt"]}'),
        throwsArgumentError,
      );
      expect(builder.termCount, 0,
          reason: 'English was loaded before the failure');
    });

    test('a resolver that throws leaves nothing behind', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder();

      expect(
        () => builder.addPreset(
          '{"languages":["en","es"]}',
          languageResolver: (String code) {
            if (code == 'es') {
              throw StateError('the network is down');
            }
            return languageSeed(code);
          },
        ),
        throwsStateError,
      );
      expect(builder.termCount, 0);
    });

    test('a bad entry undoes neither the lists nor the removals', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..useDefaultSeed();
      final int before = builder.termCount;

      expect(
        () => builder.addPreset('{"languages":["en"],"remove":["damn"],'
            '"entries":[{"t":"​"}]}'),
        throwsFormatException,
      );

      expect(builder.termCount, before);
      expect(builder.build().detect('oh damn'), isTrue,
          reason: "the preset's remove list must not have run");
    });

    test('a preset that is neither a string nor a preset is an ArgumentError',
        () {
      expect(() => VulgarityFilter.fromPreset(42), throwsArgumentError);
      expect(() => VulgarityFilter.fromPreset(<String>['en']),
          throwsArgumentError);
      expect(
          () => VulgarityFilterBuilder().addPreset(3.5), throwsArgumentError);
    });
  });

  // --------------------------------------------------------------------
  // languages: the parser checks the shape, the builder checks the supply.
  // --------------------------------------------------------------------
  group('a language code is shape-checked at parse time', () {
    for (final String code in <String>[
      'EN',
      'e',
      'toolongcode',
      'en-US',
      'e1',
      'en '
    ]) {
      test("'$code' is not a language code", () {
        expect(
          () => VulgarityPreset.parse('{"languages":["$code"]}'),
          throwsA(isA<FormatException>().having(
              (FormatException e) => e.message,
              'message',
              contains('languages'))),
        );
      });
    }

    test('a code this build does not carry still parses', () {
      // The parser must not check against the bundled list: a caller may pass
      // a resolver that serves codes no bundled pack covers.
      final VulgarityPreset preset =
          VulgarityPreset.parse('{"languages":["pt"]}');
      expect(preset.languages, <String>['pt']);
    });

    test('the builder is the one that refuses it', () {
      expect(() => VulgarityFilter.fromPreset('{"languages":["pt"]}'),
          throwsArgumentError);
    });

    test('a custom resolver can serve a code no pack covers', () {
      final VulgarityFilter filter = VulgarityFilter.fromPreset(
        '{"languages":["pt"]}',
        languageResolver: (String code) =>
            '{"profile":"fold-v1","entries":[{"t":"blorp","sev":3}]}',
      );
      expect(filter.detect('blorp'), isTrue);
    });
  });

  // --------------------------------------------------------------------
  // Who is allowed to widen a term, and who is not.
  // --------------------------------------------------------------------
  group('a term list may widen a term', () {
    // A list is a source the app author chose, so the wider rule wins. The
    // Spanish list carries "shit" with a boundary and the English list carries
    // it without one; loading Spanish must not narrow English behind the
    // author's back.
    test('a language pack does not narrow the bundled list', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..useDefaultSeed()
        ..addSeed(seedEs);

      expect(builder.hasTerm('shit'), isTrue);

      final VulgarityFilter filter = builder.build();
      final List<VulgarityMatch> found = filter.scan('shitty');
      expect(found, isNotEmpty, reason: '"shitty" stopped matching');
      expect(found.single.text, 'shit');
    });

    test('the same holds for a pack named by a preset', () {
      final VulgarityFilter filter = VulgarityFilter.fromPreset(
          '{"languages":["en","es"]}',
          languageResolver: languageSeed);

      expect(filter.scan('shitty'), isNotEmpty);
    });

    test('the French pack does not narrow it either', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..useDefaultSeed()
        ..addSeed(seedFr);

      expect(builder.build().scan('biatches'), isNotEmpty);
    });

    test('addTerm may widen, because that is the app author speaking', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..useDefaultSeed();
      expect(builder.build().detect('the class'), isFalse);

      builder.addTerm('ass', 'profanity', 1, false);
      expect(builder.build().detect('the class'), isTrue,
          reason: 'a term named in code must be able to widen a bundled one');
    });
  });

  group('a preset entry may not widen a term', () {
    test('a preset cannot drop the boundary off a bundled term', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..addTerm('ass', 'profanity', 3, true);

      // The preset repeats the term without "w". It used to widen the rule.
      builder.addPreset('{"entries":[{"t":"ass","sev":1}]}');
      final VulgarityFilter filter = builder.build();

      expect(filter.detect('the class assessment'), isFalse);
      expect(filter.detect('you ass'), isTrue);
    });

    test('a boundary the second source asks for is kept too', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..addTerm('ass', 'profanity', 3, false);
      builder.addPreset('{"entries":[{"t":"ass","sev":1,"w":true}]}');

      expect(builder.build().detect('the class'), isFalse);
    });

    test('the higher severity still wins, and the category follows it', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..addTerm('blorp', 'profanity', 2, true);
      builder.addPreset('{"entries":[{"t":"blorp","cat":"hate","sev":5}]}');

      final List<VulgarityMatch> found = builder.build().scan('you blorp');
      expect(found.single.severity, 5);
      expect(found.single.category, VulgarityCategory.hate);
      expect(builder.build().detect('xblorpx'), isFalse,
          reason: 'the boundary from the first source is still on');
    });

    test('a lower severity leaves the rating alone', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..addTerm('blorp', 'hate', 5, false);
      builder.addPreset('{"entries":[{"t":"blorp","cat":"drug","sev":1}]}');

      final List<VulgarityMatch> found = builder.build().scan('blorp');
      expect(found.single.severity, 5);
      expect(found.single.category, VulgarityCategory.hate);
    });
  });

  // --------------------------------------------------------------------
  // The seed reader and the preset reader agree on every field but one.
  // --------------------------------------------------------------------
  group('the seed reader and the preset reader agree', () {
    List<VulgarityTerm> seedTerms(String json) {
      final List<VulgarityTerm> terms = <VulgarityTerm>[];
      loadSeed(json, VulgarityFilter.profile, terms, <String>[]);
      return terms;
    }

    test('a missing severity keeps each default', () {
      // These two numbers are the one deliberate difference, and they match
      // the .NET port exactly.
      expect(seedTerms('{"entries":[{"t":"blorp"}]}').single.severity, 1);
      expect(
          VulgarityPreset.parse('{"entries":[{"t":"blorp"}]}')
              .entries
              .single
              .severity,
          3);
    });

    test('a severity that is not a whole number is refused by both', () {
      // The seed reader used to swallow this and call it severity 1, while the
      // preset reader called the same document severity 3.
      expect(() => seedTerms('{"entries":[{"t":"x","sev":3.0}]}'),
          throwsFormatException);
      expect(() => VulgarityPreset.parse('{"entries":[{"t":"x","sev":3.0}]}'),
          throwsFormatException);
    });

    const Map<String, String> badSeed = <String, String>{
      '{"schema":"1","entries":[]}': 'schema',
      '{"schema":1.0,"entries":[]}': 'schema',
      '{"profile":5,"entries":[]}': 'profile',
      '{"entries":[{"t":"x","cat":5}]}': 'cat',
      '{"entries":[{"t":"x","w":"yes"}]}': 'w',
      '{"entries":[{"t":"x","sev":"3"}]}': 'sev',
      '{"entries":[],"allow":[5]}': 'allow',
      '{"entries":[],"allow":"blorp"}': 'allow',
    };

    badSeed.forEach((String json, String field) {
      test('a seed with a bad $field is refused: $json', () {
        expect(
          () => seedTerms(json),
          throwsA(isA<FormatException>().having(
              (FormatException e) => e.message, 'message', contains(field))),
        );
      });
    });

    test('a missing category is still "other" on both', () {
      expect(seedTerms('{"entries":[{"t":"blorp"}]}').single.categoryName,
          'other');
      expect(
          VulgarityPreset.parse('{"entries":[{"t":"blorp"}]}')
              .entries
              .single
              .categoryName,
          'other');
    });

    test('an unknown category name is still tolerated on both', () {
      // A NAME the build does not know stays lenient. A category of the wrong
      // TYPE does not. The two are different problems.
      expect(
          seedTerms('{"entries":[{"t":"blorp","cat":"brandnew"}]}')
              .single
              .category,
          VulgarityCategory.other);
    });
  });

  // --------------------------------------------------------------------
  // A term that folds to nothing.
  // --------------------------------------------------------------------
  group('a term that folds to nothing', () {
    test('is a FormatException from a seed document', () {
      expect(() => VulgarityFilter.fromSeed('{"entries":[{"t":"..."}]}'),
          throwsFormatException);
    });

    test('is a FormatException from a preset', () {
      expect(() => VulgarityFilter.fromPreset('{"entries":[{"t":"​"}]}'),
          throwsFormatException);
    });

    test('is a FormatException from a pack', () {
      final List<int> pack = _buildPack(<int>[
        1, 0, // schema, flags
        ..._text('fold-v1'),
        1, ..._text('other'), // one category
        1, ..._text('...'), 0x01, // one entry: category 0, severity 1
      ]);

      expect(() => VulgarityFilterBuilder().addSeedBytes(pack),
          throwsFormatException);
    });

    test('is an ArgumentError from addTerm', () {
      // A caller who names a term in code got the argument wrong. That is not
      // a malformed document.
      expect(
          () => VulgarityFilterBuilder().addTerm('...', 'profanity', 1, true),
          throwsArgumentError);
    });
  });

  // --------------------------------------------------------------------
  // Varints are capped at 31 bits, so every runtime reads the same pack.
  // --------------------------------------------------------------------
  group('a pack number wider than 31 bits is refused', () {
    // Left uncapped, dart2js truncates the shift to 32 bits and reads a
    // different number from the Dart VM, so a crafted pack would load on the
    // web and fail on the VM.
    const Map<String, List<int>> crafted = <String, List<int>>{
      '85 80 80 80 10': <int>[0x85, 0x80, 0x80, 0x80, 0x10],
      'FF FF FF FF 0F': <int>[0xFF, 0xFF, 0xFF, 0xFF, 0x0F],
    };

    crafted.forEach((String name, List<int> bytes) {
      test('a count of $name', () {
        final List<int> pack = _buildPack(<int>[
          1, 0, // schema, flags
          ..._text('fold-v1'),
          ...bytes, // the category count
        ]);

        expect(
          () => VulgarityFilterBuilder().addSeedBytes(pack),
          throwsA(isA<FormatException>().having(
              (FormatException e) => e.message,
              'message',
              contains('runs too long'))),
        );
      });
    });

    test('a pack the encoder would write still reads', () {
      // The control. If this failed, the cap would be refusing real packs.
      final List<int> pack = _buildPack(<int>[
        1,
        0,
        ..._text('fold-v1'),
        1,
        ..._text('profanity'),
        1,
        ..._text('blorp'),
        0x03,
      ]);

      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..addSeedBytes(pack);
      expect(builder.termCount, 1);
      expect(builder.build().scan('blorp').single.severity, 3);
    });
  });

  // --------------------------------------------------------------------
  // Pack text: the shapes a sender actually produces.
  // --------------------------------------------------------------------
  group('base64 pack text is normalised before it is decoded', () {
    final String packText = languageSeed('en');

    void expectLoads(String text, String shape) {
      expect(tryReadPackText(text), isNotNull, reason: 'refused $shape');

      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..addSeed(text);
      expect(builder.termCount, greaterThan(0),
          reason: 'addSeed refused $shape');
    }

    test('padding left off', () {
      expectLoads(packText.replaceAll('=', ''), 'base64 with the padding cut');
    });

    test('URL-safe and unpadded', () {
      // This is what base64url produces, and what a pack in a URL looks like.
      expectLoads(
        packText.replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', ''),
        'unpadded base64url',
      );
    });

    test('a leading byte-order mark', () {
      expectLoads('\u{FEFF}$packText', 'a pack behind a BOM');
    });

    test('a byte-order mark and a newline', () {
      // What a text editor writes when it saves the pack as UTF-8 with a BOM.
      expectLoads('\u{FEFF}\n$packText\n', 'a pack behind a BOM and a newline');
    });

    test('a BOM in the middle is still corruption', () {
      final int half = packText.length ~/ 2;
      expect(
        tryReadPackText(
            '${packText.substring(0, half)}\u{FEFF}${packText.substring(half)}'),
        isNull,
      );
    });

    test('a seed document behind a BOM still takes the JSON path', () {
      final VulgarityFilterBuilder builder = VulgarityFilterBuilder()
        ..addSeed('\u{FEFF}\n{"profile":"fold-v1",'
            '"entries":[{"t":"blorp","sev":3}]}');
      expect(builder.termCount, 1);
    });

    test('text that is neither is still refused clearly', () {
      expect(
        () => VulgarityFilterBuilder().addSeed('VlBLMQ but not really'),
        throwsFormatException,
      );
      expect(() => VulgarityFilterBuilder().addSeed(''), throwsFormatException);
    });
  });

  // --------------------------------------------------------------------
  // Language codes arrive upper-cased more often than anyone expects.
  // --------------------------------------------------------------------
  group('languageSeed is case-insensitive', () {
    test('English in any case', () {
      expect(languageSeed('EN'), languageSeed('en'));
      expect(languageSeed('En'), languageSeed('en'));
    });

    test('a pack in any case', () {
      expect(languageSeed('ES'), languageSeed('es'));
      expect(languageSeed('Zh'), languageSeed('zh'));
    });

    test('a code no pack covers is still an ArgumentError', () {
      expect(() => languageSeed('XX'), throwsArgumentError);
    });
  });
}

/// Encodes [text] as a pack string: a varint length, then the UTF-8 bytes.
List<int> _text(String text) {
  final List<int> raw = utf8.encode(text);
  return <int>[raw.length, ...raw];
}

/// Wraps a pack body in the magic and the RC4 mask.
///
/// This mirrors `tool/packlib.py`, so a test can hand the reader bytes no
/// encoder would ever write.
List<int> _buildPack(List<int> body) {
  const List<int> key = <int>[
    0x76, 0x75, 0x6C, 0x67, 0x61, 0x72, 0x69, 0x74, 0x79, // "vulgarity"
    0x2D, 0x70, 0x61, 0x63, 0x6B, // "-pack"
    0x2D, 0x76, 0x31, // "-v1"
  ];

  final List<int> box = List<int>.generate(256, (int i) => i);
  int j = 0;
  for (int i = 0; i < 256; i++) {
    j = (j + box[i] + key[i % key.length]) & 0xFF;
    final int swap = box[i];
    box[i] = box[j];
    box[j] = swap;
  }

  final List<int> out = <int>[0x56, 0x50, 0x4B, 0x31]; // "VPK1"
  int a = 0;
  int b = 0;
  for (int i = 0; i < body.length; i++) {
    a = (a + 1) & 0xFF;
    b = (b + box[a]) & 0xFF;
    final int swap = box[a];
    box[a] = box[b];
    box[b] = swap;
    out.add(body[i] ^ box[(box[a] + box[b]) & 0xFF]);
  }
  return out;
}
