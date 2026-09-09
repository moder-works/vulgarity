/// Trie-based vulgarity detection, filtering and scoring.
///
/// The filter folds text to profile `fold-v1` before it matches, so leetspeak
/// (`sh!t`), separator evasion (`f.u.c.k`) and repeated letters (`fuuuck`) all
/// reach the same term. A word-boundary test keeps ordinary words clean, so
/// "Scunthorpe", "assassin" and "the class" never flag.
///
/// The matching .NET package reads the same seed file and the same test
/// vectors, so both runtimes reach the same verdict on the same text.
///
/// ```dart
/// import 'package:vulgarity/vulgarity.dart';
///
/// void main() {
///   final filter = VulgarityFilter.createDefault();
///
///   print(filter.detect('what the f.u.c.k'));  // true
///   print(filter.filter('what the f.u.c.k'));  // what the *******
///   print(filter.score('what the f.u.c.k'));   // 4
///
///   for (final match in filter.scan('what the f.u.c.k')) {
///     print('${match.start}..${match.end} ${match.text} ${match.severity}');
///   }
/// }
/// ```
///
/// To add a language, import its pack and pass it to the builder. See
/// `package:vulgarity/lang/<code>.dart`.
library;

export 'src/model/vulgarity_category.dart';
export 'src/model/vulgarity_match.dart';
export 'src/model/vulgarity_term.dart';
export 'src/vulgarity_filter.dart';
export 'src/vulgarity_filter_builder.dart';
export 'src/vulgarity_options.dart';
