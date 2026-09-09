# Vulgarity

Trie-based vulgarity detection, filtering and scoring for **.NET** and **Dart**.

One term list. One fold table. One set of test vectors. Both runtimes read the
same files, so a Flutter client and a .NET backend reach the same verdict on the
same text.

```
detect  ->  is this text vulgar?
scan    ->  every match, with offsets into the ORIGINAL text
filter  ->  the text with each match masked
score   ->  a severity number the caller can threshold
```

---

## What it defeats

| Evasion | Example | How |
| --- | --- | --- |
| Case | `SHIT` | lowercase fold |
| Leetspeak | `sh1t`, `a$$`, `f@ck` | 11 symbol and digit folds |
| Separators | `f.u.c.k`, `f u c k` | punctuation and spaces dropped |
| Repeated letters | `fuuuuck` | a second scan with runs collapsed |
| Accents | `fúck` | 600 folds down to plain ASCII |
| Homoglyphs | `сосk` (Cyrillic), `ｆｕｃｋ` (fullwidth) | Cyrillic, Greek and 35 styled alphabets |
| Zero-width | `f<ZWSP>uck` | invisible characters dropped |

And what it must **not** flag:

```
Scunthorpe    assassin      the class      analysis     grapefruit
raccoon       cockpit       sauerkraut     niggardly    Shiite
therapist     thorny        heroine        trimming     mushrooms
```

The last row matters. `therapist` stays clean, and **`the rapist` is flagged** —
the same letters, told apart by the space between the words.

---

## Install

**.NET** — targets `netstandard2.0` and `net8.0`.

```bash
dotnet add package Vulgarity
```

**Dart** — pure Dart, so it runs on a server, a CLI, Flutter, and Flutter web.

```yaml
dependencies:
  vulgarity: ^0.1.0
```

---

## Use it

```csharp
using Vulgarity;

// Build once and keep it. Compiling the trie costs far more than a scan.
var filter = VulgarityFilter.CreateDefault();

bool   dirty = filter.Detect("what the f.u.c.k");   // true
string clean = filter.Filter("what the f.u.c.k");   // "what the *******"
int    score = filter.Score("what the f.u.c.k");    // 4

foreach (VulgarityMatch m in filter.Scan("what the f.u.c.k"))
{
    // 9..16  "f.u.c.k"  ->  fuck  (profanity, 4)
    Console.WriteLine($"{m.Start}..{m.End} {m.Excerpt(text)} -> {m.Text} {m.Severity}");
}
```

```dart
import 'package:vulgarity/vulgarity.dart';

final filter = VulgarityFilter.createDefault();

final dirty = filter.detect('what the f.u.c.k');   // true
final clean = filter.filter('what the f.u.c.k');   // what the *******
final score = filter.score('what the f.u.c.k');    // 4

for (final m in filter.scan('what the f.u.c.k')) {
  print('${m.start}..${m.end} ${m.excerpt(text)} -> ${m.text} ${m.severity}');
}
```

A `VulgarityFilter` holds no mutable state, so many threads can scan through one
instance at the same time.

### Offsets point at the original text

`Start` and `End` index the string you passed in, not the folded form. So you can
highlight the exact span, emoji and accents included.

```csharp
var hit = filter.Scan("café 🙂 f.u.c.k end")[0];
hit.Excerpt("café 🙂 f.u.c.k end");   // "f.u.c.k"
```

---

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `MinSeverity` | `1` | Ignore terms below this severity. Set `2` to drop clinical anatomy. |
| `Categories` | all | Restrict matching to a set of categories. |
| `MaskChar` | `*` | The character `Filter` repeats. |
| `MaskToken` | none | A fixed replacement string. It overrides `MaskChar`. |
| `RepeatTolerance` | `true` | Run the second scan that catches `fuuuck`. |
| `CollapseContained` | `true` | Drop a match that sits inside a longer match. |
| `ScoreMode` | `Total` | `Total` sums severities. `Max` takes the highest. |

```csharp
var strict = filter.WithOptions(new VulgarityOptions
{
    MinSeverity = 3,
    Categories = new HashSet<VulgarityCategory> { VulgarityCategory.Hate },
    MaskToken = "[removed]",
});
```

```dart
final strict = filter.withOptions(VulgarityOptions(
  minSeverity: 3,
  categories: {VulgarityCategory.hate},
  maskToken: '[removed]',
));
```

`WithOptions` reuses the compiled trie, so it is cheap.

### Adding your own terms

```csharp
var filter = new VulgarityFilterBuilder()
    .UseDefaultSeed()
    .AddTerm("brandname", "profanity", 2, requireBoundary: true)
    .AddAllow("brandnameshire")
    .Build();
```

```dart
final filter = (VulgarityFilterBuilder()
      ..useDefaultSeed()
      ..addTerm('brandname', 'profanity', 2, true)
      ..addAllow('brandnameshire'))
    .build();
```

The builder folds every term you add, so `BR@NDNAME` and `brandname` reach the
trie as the same pattern.

---

## Languages

English is hand-curated: **526 terms**, each with a category, a severity, and a
word-boundary rule. It loads by default.

Fourteen more packs ship, **off by default**:

| | | | | | | |
| --- | --- | --- | --- | --- | --- | --- |
| Arabic | German | Spanish | Persian | French | Hindi | Italian |
| Korean | Dutch | Polish | Russian | Thai | Vietnamese | Chinese |

```csharp
var filter = new VulgarityFilterBuilder()
    .UseDefaultSeed()
    .UseLanguage("es")
    .Build();
```

```dart
import 'package:vulgarity/lang/es.dart';

final filter = (VulgarityFilterBuilder()
      ..useDefaultSeed()
      ..addSeed(seedEs))
    .build();
```

> **Read this before you load a pack.** Only English is vetted. The other 14 come
> from a community list that nobody checked term by term, and the source carries
> no category or severity, so every entry takes `profanity` and severity 3.
> **Expect false positives.** Load a pack only for text in that language.

Each Dart pack is its own library, so a pack you never import stays out of your
build. Importing `package:vulgarity/languages.dart` pulls in all 14 at once —
convenient on a server, wasteful in an app.

---

## How it works

Three layers, written twice, identical in both languages.

### 1. The normalizer

It folds the text to profile `fold-v1` and keeps a map back to the original
offsets. For each folded character it records four things:

- `chars` — the folded code point
- `srcStart` / `srcEnd` — where it came from in the original string
- `hard` — was the source a real word character? A leetspeak stand-in such as
  `@` folds to a letter but stays **soft**
- `gap` — was a word-breaking separator dropped just before it?

`hard` and `gap` are what make the word-boundary test work. Read on.

### 2. The automaton

An Aho-Corasick trie — a prefix trie with failure links. One pass finds every
term, so the cost stays linear in the length of the text. A second automaton
holds the allowlist.

### 3. Repeated letters, and why the seed is never squeezed

Collapsing repeats **inside the term list** is wrong. It rewrites `ass` to `as`,
and the filter then flags the ordinary word "as".

Collapsing repeats **inside the input** is safe, because a shorter input can
never match a longer pattern. So the scanner builds two streams:

- **Stream A** — folded, repeats intact
- **Stream B** — stream A with every run collapsed to one character

| Text | Stream A | Stream B | `fuck` | `ass` |
| --- | --- | --- | --- | --- |
| `fuck` | `fuck` | `fuck` | hit in A | — |
| `fuuuck` | `fuuuck` | `fuck` | hit in B | — |
| `ass` | `ass` | `as` | — | hit in A |
| `as` | `as` | `as` | — | **no hit** |

Stream A always wins. Stream B widens a span across the letters it collapsed, so
in `fuck this shit` it would report `s shit` for the second term. Stream A
already holds the tight span, so the loose duplicate is dropped.

Set `RepeatTolerance = false` to skip stream B entirely.

### 4. Word boundaries

A term marked `"w": true` must sit on a word boundary. An edge counts as a
boundary when the match reaches the end of the text, when a separator was dropped
there (`gap`), or when the neighbour is not a real word character (`hard`).

That one rule does almost all the false-positive work:

| Text | Term | Left edge | Result |
| --- | --- | --- | --- |
| `an ass` | `ass` | a space was dropped | **flag** |
| `bass` | `ass` | `b` is a word character | clean |
| `assassin` | `ass` | right edge is `a` | clean |
| `the rapist` | `rapist` | a space was dropped | **flag** |
| `therapist` | `rapist` | `e` is a word character | clean |
| `f.u.c.k!` | `fuck` | `!` folds soft, not hard | **flag** |

The allowlist only handles what this cannot. It holds **16 entries**, and a test
fails on any entry the matcher does not need. That test exists because
`therapist` once sat in the allowlist and suppressed the correct match on
`the rapist`.

---

## Repository layout

```
data/                     the contract. Neither language owns it.
  fold-v1.json            the fold table
  seed.json               the curated English list
  seed.<lang>.json        14 optional packs
  vectors.json            38 behavioural cases both ports must reproduce
  fold-vectors.json       35 normalizer cases
  testdata/               the community corpus, used only by tests

tool/                     generators, all idempotent, all with --check
  gen_fold_table.py       fold-v1.json + the C# and Dart tables
  gen_seed.py             seed.json
  gen_lang_packs.py       seed.<lang>.json
  gen_dart_seeds.py       the Dart seed libraries
  foldlib.py              a Python reference fold, used by the tools

dotnet/src/Vulgarity/     the C# library
dotnet/tests/             200 tests
dart/lib/                 the Dart library
dart/test/                210 tests
```

`data/` is generated, and it is also the source of truth at run time. Change a
generator, run it, then run both test suites.

---

## Verify

```bash
# .NET
dotnet test dotnet/tests/Vulgarity.Tests/Vulgarity.Tests.csproj

# Dart
cd dart && dart test

# The generated files are current
python3 tool/gen_fold_table.py --check
python3 tool/gen_dart_seeds.py --check

# Both ports agree, byte for byte
dotnet build dotnet/example/Example.csproj
dotnet dotnet/example/bin/Debug/net8.0/Example.dll "sh!t happens" > /tmp/a
cd dart && dart run example/vulgarity_example.dart "sh!t happens" > /tmp/b
diff /tmp/a /tmp/b
```

What the suites check:

- **Parity** — both ports read `data/vectors.json` and must produce identical
  detect, scan, filter and score results.
- **The normalizer** — both ports read `data/fold-vectors.json` and must produce
  identical characters, offsets, `hard` flags and `gap` flags.
- **The fold table** — the compiled C# and Dart tables must equal
  `data/fold-v1.json`.
- **False positives** — both ports sweep the system word list (235,762 words).
  A word that only the squeeze pass flags fails the build, unless the allowlist
  covers it.
- **Evasion** — 1,013 real evasion spellings from the community corpus
  (`$h!t`, `4r5e`, `a$$h0le`, `.f uc k`) must all be detected.
- **The allowlist** — every entry must be one the matcher genuinely needs.
- **Language packs** — each pack loads, targets this profile, is marked
  unvetted, and finds every term it carries.

---

## Changing the term list

1. Edit `tool/gen_seed.py`.
2. Run `python3 tool/gen_seed.py`.
3. Run `python3 tool/gen_dart_seeds.py`.
4. Run both test suites.

The false-positive sweep will tell you if a new term fires on ordinary English.
Fix it by marking the term `w` (require a word boundary) or by adding the
innocent word to `ALLOW`.

To swap the whole list, write your own `seed.json` against this schema:

```json
{
  "schema": 1,
  "profile": "fold-v1",
  "entries": [
    { "t": "badword", "cat": "profanity", "sev": 3, "w": true }
  ],
  "allow": ["innocentword"]
}
```

`profile` is the safety catch. Each runtime checks it at load, so a seed file
built against an older fold table fails loudly instead of matching silently
wrong.

---

## Limits

- **English only, by default.** The other packs are unvetted. See above.
- **`chink` and `dyke` are ordinary words too.** Both are slurs and both stay in
  the list. Add them to your own allowlist if your domain needs them.
- **Cross-word joins.** Separators are dropped, so `mega ssuck` folds to
  `megassuck`. The word-boundary rule catches most of this, not all of it.
- **No wildcard folding.** `p*ssy` does not match, because `*` could be any
  letter. `pu$$y` and `p.u.s.s.y` both do.
- **The styled-alphabet ranges have holes.** A few Unicode maths letters live
  outside their block. A miss, never a false positive.

---

## Credits

The multi-language term lists and the English evasion corpus come from
[safe_text](https://github.com/master-wayne7/safe_text) by Ronit Rameja, MIT
licensed. The English seed here is hand-curated, and the corpus serves only as a
test fixture.

## Licence

MIT.
