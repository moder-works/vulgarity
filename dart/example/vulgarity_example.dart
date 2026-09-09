// Run: dart run example/vulgarity_example.dart
//
// Pass your own text as arguments to check it:
//   dart run example/vulgarity_example.dart "some text here"

import 'package:vulgarity/vulgarity.dart';

// Mild on purpose. This file is published, and pub.dev renders it on the
// package page, so the samples use only severity-1 terms. They still show
// every evasion the matcher defeats.
const List<String> samples = <String>[
  'Have a nice day.',
  'you are a d.a.m.n',
  'what the daaaamn',
  'h3ll happens',
  r'cr@p',
  'I live in Scunthorpe',
  'he is an assassin',
  'class of 2024',
  'a hell of a day',
  'a nice shell company',
  'thorny problem, heroine of the story, trimming the hedge',
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
