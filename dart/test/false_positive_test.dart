import 'package:test/test.dart';
import 'package:vulgarity/vulgarity.dart';

import 'test_data.dart';

/// These two words are squeeze artifacts, and both stay flagged on purpose.
/// Every other artifact belongs in the seed allowlist.
const Set<String> knownVulgarArtifacts = <String>{'kaffir', 'bastaard'};

const List<String> ordinaryEnglish = <String>[
  'thorny problem',
  'the heroine of the story',
  'trimming the hedge',
  'a scrappy team',
  'scumming the pot',
  'a looser fit',
  'rapping on the door',
  'the annals of history',
  'cattle inbreed here',
  'pollack for dinner',
  'the poorness of the soil',
  'a Shiite mosque',
  'skeet shooting',
  'mushrooms on toast',
  'shiitake mushrooms',
  'I live in Scunthorpe',
  'he is an assassin',
  'the class of 2024',
  'an analysis of the data',
  'grapefruit juice',
  'a raccoon in the yard',
  'the cockpit door',
  'sauerkraut and sausage',
  'a niggardly sum',
  'my therapist is great',
  'multivibrator circuit',
  'spasticity in the muscle',
  'bass guitar',
  'assume the position',
  'Cockburn is a surname',
];

void main() {
  final VulgarityFilter filter = VulgarityFilter.createDefault();

  group('ordinary English stays clean', () {
    for (final String text in ordinaryEnglish) {
      test(text, () {
        final List<VulgarityMatch> hits = filter.scan(text);
        expect(hits, isEmpty,
            reason: "'$text' flagged "
                '${hits.map((VulgarityMatch h) => h.term.text).join(', ')}');
      });
    }
  });

  test('the squeeze pass invents no new match', () {
    final List<String>? lines = englishWords();
    if (lines == null) {
      return; // This machine carries no word list.
    }

    final VulgarityFilter noSqueeze =
        filter.withOptions(VulgarityOptions(repeatTolerance: false));
    final RegExp plain = RegExp(r'^[a-z]+$');
    final List<String> unexpected = <String>[];

    for (final String line in lines) {
      final String word = line.trim().toLowerCase();
      if (word.length < 4 || !plain.hasMatch(word)) {
        continue;
      }
      if (noSqueeze.detect(word) || knownVulgarArtifacts.contains(word)) {
        continue;
      }
      if (filter.detect(word)) {
        unexpected.add(word);
      }
    }

    expect(unexpected, isEmpty,
        reason: 'The squeeze pass flagged ordinary words. Add each one to the '
            'seed allowlist, or mark its term with "w".');
  });
}
