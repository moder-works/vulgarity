# vulgarity

Trie-based vulgarity detection, filtering and scoring for Dart.

It defeats leetspeak, separator evasion and repeated letters, and it keeps
ordinary words clean. [The .NET port in the same repository][dotnet] reads the
same term lists and the same test vectors, so both runtimes reach the same
verdict on the same text.

Pure Dart, with no runtime dependencies. It runs on a server, a CLI, Flutter,
and Flutter web.

## Install

```yaml
dependencies:
  vulgarity: ^0.1.0
```

## Use it

Build the filter once and keep it. Compiling the trie costs far more than a
scan, and the filter is immutable and safe to share.

The examples use mild terms on purpose. The bundled list runs to severity 5,
and `score` rises with it.

```dart
import 'package:vulgarity/vulgarity.dart';

final filter = VulgarityFilter.createDefault();

filter.detect('what the d.a.m.n');   // true
filter.filter('what the d.a.m.n');   // what the *******
filter.score('what the d.a.m.n');    // 1

const text = 'what the d.a.m.n';
for (final match in filter.scan(text)) {
  print('${match.start}..${match.end} ${match.excerpt(text)} '
      '${match.term.text} ${match.severity}');
  // 9..16 d.a.m.n damn 1
}
```

`start` and `end` index the string you passed in, not the folded form. So you
can highlight the exact span, emoji and accents included. `excerpt` gives you
that span back; `term.text` gives you the list entry it reached, folded to
`fold-v1`. Show the excerpt to a person, and group your counts by the term.

## What it defeats

| Evasion | Example | How |
| --- | --- | --- |
| Case | `CRAP` | lowercase fold |
| Leetspeak | `d4mn`, `h3ll`, `cr@p` | 11 symbol and digit folds |
| Separators | `d.a.m.n`, `d a m n` | punctuation and spaces dropped |
| Repeated letters | `daaaamn` | a second scan with runs collapsed |
| Accents | `dámn` | 600 folds down to plain ASCII |
| Homoglyphs | `сrар` (Cyrillic), `ｄａｍｎ` (fullwidth) | Cyrillic, Greek and 35 styled alphabets |
| Zero-width | `d<ZWSP>amn` | invisible characters dropped |

And what it must **not** flag:

```
Scunthorpe    assassin      the class      analysis     grapefruit
raccoon       cockpit       sauerkraut     scrappy      Shiite
shell         thorny        heroine        trimming     mushrooms
```

A word-boundary test does almost all of that work. `shell` stays clean, and
`a hell` is flagged — the same letters, told apart by the space between the
words.

## Options

```dart
final filter = VulgarityFilter.createDefault(const VulgarityOptions(
  minSeverity: 2,
  maskToken: '[removed]',
  scoreMode: ScoreMode.max,
));
```

| Option | Default | Effect |
| --- | --- | --- |
| `minSeverity` | `1` | Ignore terms below this severity. Set `2` to drop clinical anatomy. |
| `categories` | all | Restrict matching to a set of categories. |
| `maskChar` | `*` | The character `filter` repeats. |
| `maskToken` | none | A fixed replacement string. It overrides `maskChar`. |
| `repeatTolerance` | `true` | Run the second scan that catches `daaamn`. |
| `collapseContained` | `true` | Drop a match that sits inside a longer match. |
| `scoreMode` | `total` | `total` sums severities. `max` takes the highest. |

Use `withOptions` to change the policy without rebuilding the trie.

## Languages

English is hand-curated and always loads. Fourteen more packs ship with the
package.

> **Read this before you load a pack.** Only English is vetted. The other 14
> come from a community list that nobody checked term by term, and the source
> carries no category or severity, so every entry takes `profanity` and
> severity 3. **Expect false positives.** Load a pack only for text in that
> language.

Each pack is its own library, so a pack you never import stays out of your
build:

```dart
import 'package:vulgarity/lang/es.dart';

final filter = (VulgarityFilterBuilder()
      ..useDefaultSeed()
      ..addSeed(seedEs))
    .build();
```

Importing `package:vulgarity/languages.dart` pulls in all 14 at once. That is
convenient on a server and wasteful in an app.

## Presets: policy as data

A **preset** carries a whole policy — options, extra terms, an allowlist, and
terms to drop. So a server can change how strict a client is with no app
release.

```dart
final response = await http.get(Uri.parse('https://example.com/policy.json'));
final filter = VulgarityFilter.fromPreset(VulgarityPreset.parse(response.body));
```

`VulgarityPreset.parse` is where a document becomes a policy, so that is where
a bad one stops. Everything downstream takes the parsed object.

A preset that names a language other than `en` needs a resolver, so that an
unimported pack stays out of your build:

```dart
import 'package:vulgarity/languages.dart';

final filter = VulgarityFilter.fromPreset(
  VulgarityPreset.parse(json),
  languageResolver: languageSeed,
);
```

Treat a preset from the network as untrusted. A malformed document is refused
whole, and never applies in part.

## The term lists carry no readable words

The bundled lists ship as **packs**: the same terms in a compact record format,
XOR-masked. A compiled app therefore carries no readable term, and nothing in
this package's source shows one. The payload is also 6.6 times smaller than the
JSON it replaces.

**This is obfuscation, not encryption.** The key ships beside the data, and the
authored lists are in the repository in plain text. Anyone who wants the list
can still have it. The goal is only that nobody meets it by accident.

`addSeed` reads the format from the input itself, so nothing changes for a
caller. It accepts a JSON document, a pack as base64 text, or a pack as raw
bytes through `addSeedBytes`. To host your own list without plain words in
transit, pack it with `tool/pack.py` from the repository.

One caution: `languageSeed` still returns a `String`, so every caller that only
forwards the value keeps working. The value is a pack, not JSON. Code that ran
`jsonDecode` on it must call `addSeed` instead.

## Limits

- **English only, by default.** The other packs are unvetted. See above.
- **A few slurs are ordinary words too.** The list holds several entries that
  are also everyday English in another sense. They stay in the list, because
  the slur is the common reading. Add them to your own allowlist if your domain
  needs the other one.
- **Cross-word joins.** Separators are dropped, so `mega ssuck` folds to
  `megassuck`. The word-boundary rule catches most of this, not all of it.
- **No wildcard folding.** A `*` could stand for any letter, so a term spelled
  with one does not match. Symbol and separator evasions do.

## Credits

The multi-language term lists and the English evasion corpus come from
[safe_text](https://github.com/master-wayne7/safe_text) by Ronit Rameja, MIT
licensed. The English list here is hand-curated, and the corpus serves only as
a test fixture.

## Licence

MIT.

[dotnet]: https://github.com/moder-works/vulgarity/tree/main/dotnet
