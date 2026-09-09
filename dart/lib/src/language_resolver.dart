/// Returns the bundled term list for a language code.
///
/// This package compiles in English only, so a preset that names any other
/// language needs one of these. Pass `languageSeed` from
/// `package:vulgarity/languages.dart`, which resolves every code this package
/// carries, or write your own to load a pack from disk or from a server.
///
/// The value it returns is whatever `addSeed` accepts: a seed document as
/// JSON, or a pack as base64 text.
///
/// ```dart
/// import 'package:vulgarity/languages.dart';
///
/// const LanguageResolver resolver = languageSeed;
/// ```
///
/// Throw [ArgumentError] for a code you cannot resolve.
typedef LanguageResolver = String Function(String code);
