/// Trie-based vulgarity detection, filtering and scoring.
///
/// The filter folds text to profile `fold-v1` before it matches, so leetspeak
/// (`h3ll`), separator evasion (`d.a.m.n`) and repeated letters (`daaamn`) all
/// reach the same term. A word-boundary test keeps ordinary words clean, so
/// "Scunthorpe", "assassin" and "the class" never flag.
///
/// The matching .NET package reads the same term list and the same test
/// vectors, so both runtimes reach the same verdict on the same text.
///
/// The examples here use mild terms on purpose. The real list runs to severity
/// 5, and `score` rises with it.
///
/// ```dart
/// import 'package:vulgarity/vulgarity.dart';
///
/// void main() {
///   final filter = VulgarityFilter.createDefault();
///
///   print(filter.detect('what the d.a.m.n'));  // true
///   print(filter.filter('what the d.a.m.n'));  // what the *******
///   print(filter.score('what the d.a.m.n'));   // 1
///
///   for (final match in filter.scan('what the d.a.m.n')) {
///     print('${match.start}..${match.end} '
///         '${match.term.text} ${match.severity}');
///   }
/// }
/// ```
///
/// To add a language, import its pack and pass it to the builder. See
/// `package:vulgarity/lang/<code>.dart`.
library;

export 'src/language_resolver.dart';
export 'src/model/vulgarity_category.dart';
export 'src/model/vulgarity_match.dart';
export 'src/model/vulgarity_term.dart';
export 'src/vulgarity_filter.dart';
export 'src/vulgarity_options.dart';
export 'src/vulgarity_preset.dart';
