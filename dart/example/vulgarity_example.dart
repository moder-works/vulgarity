// Run: dart run example/vulgarity_example.dart
//
// Pass your own text as arguments to check it:
//   dart run example/vulgarity_example.dart "some text here"

import 'package:vulgarity/vulgarity.dart';

const List<String> samples = <String>[
  'Have a nice day.',
  'you are a f.u.c.k',
  'what the fuuuuck',
  'sh!t happens',
  'I live in Scunthorpe',
  'he is an assassin',
  'an ass',
  'the rapist was caught',
  'my therapist is great',
  r'a$$hole',
  'thorny heroine trimming scrappy',
];

void main(List<String> args) {
  final VulgarityFilter filter = VulgarityFilter.createDefault();
  print('profile: ${VulgarityFilter.profile}, terms: ${filter.termCount}');

  for (final String text in args.isNotEmpty ? args : samples) {
    print('');
    print('  in    : $text');
    print('  detect: ${filter.detect(text)}   score: ${filter.score(text)}');
    print('  filter: ${filter.filter(text)}');
    for (final VulgarityMatch hit in filter.scan(text)) {
      print('    [${hit.start}..${hit.end}] '
          "'${hit.excerpt(text)}' -> ${hit.text} "
          '(${hit.categoryName}, sev ${hit.severity})');
    }
  }
}
