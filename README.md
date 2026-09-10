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

## Glossary

The words below mean one thing each, everywhere in this project.

### How the pieces fit

```
  data/seed.json          A SEED: the authored list. Plain JSON, reviewable.
        |                 Holds TERMS and an ALLOWLIST.
        |  tool/gen_packs.py
        v
  data/packs/seed-en.vpk  A PACK: the same list, compact and masked.
        |                 This is what ships. It holds no readable word.
        |  embedded in the assembly, or base64 in Dart source
        v
  PackReader              Decodes the pack once, when you build the filter.
        |
        v
  VulgarityFilter         Holds a TRIE of folded terms.
        ^
        |  every input is FOLDED first, by the FOLD TABLE
        |
  "what the d.a.m.n"  ->  fold  ->  "whatthedamn"  ->  match
```

A **PRESET** sits above all of it: a policy document naming which packs to load
and how strict to be.

### The terms

| Term | What it means |
| --- | --- |
| **Term** | One entry in a list. Always stored already folded, so `damn`, never `D4MN`. Carries a category, a severity and a boundary flag. |
| **Seed**, seed document | The authored list, as JSON: `data/seed.json` and `data/seed.<lang>.json`. Human-readable and reviewable. It is the source, not the thing that ships. |
| **Pack**, `.vpk` | A seed compiled into a compact binary and masked, so no readable word survives. This is what ships inside the compiled app. `data/packs/seed-<code>.vpk`. |
| **Magic** | The four bytes `VPK1` that open every pack. Never masked, so a reader can recognise the format without the key. |
| **Mask**, keystream | The XOR pass that makes a pack unreadable. It is **obfuscation, not encryption** — the key ships in the client. |
| **Fold**, folding | Reducing text to a plain, comparable form before matching: lowercase, strip accents, resolve leetspeak, drop separators. `d.á.M.N` and `DAMN` both fold to `damn`. |
| **Fold table** | The data that defines exactly how folding works. `data/fold-v1.json`, compiled into both ports. |
| **Fold profile** | The version of that contract, currently `fold-v1`. **A pack built for one profile cannot be loaded by a client implementing another.** Both ports refuse it rather than match wrongly. |
| **Stream A / Stream B** | The two passes of a scan. A is the folded text. B is A with repeated letters collapsed, which catches `daaaamn`. A always wins when both hit. |
| **Word boundary** | A term marked `"w": true` matches only when it stands alone. This is what keeps `shell` clean while `a hell` flags. |
| **Allowlist** | Innocent words the filter must never flag, such as `Scunthorpe` and `shiitake`. The last resort, for what the boundary rule cannot handle. |
| **Category** | What kind of term it is: `profanity`, `sexual`, `hate`, `violence`, `drug`, `other`. An unknown name becomes `other`. |
| **Severity** | How bad, from 1 to 5. Clinical anatomy sits at 1. `minSeverity` filters on it, `score` sums or maxes it. |
| **Language pack** | One of the 14 optional non-English lists. All are community-sourced and **unvetted**. Only English is reviewed. |
| **Preset** | A policy as data: which languages to load, the options, terms to add, an allowlist, and terms to drop. A server can change one with no app release. |
| **Schema** | The container version of a seed, pack or preset. Currently `1` for all three. A mismatch is refused, not adapted. |
| **Trie**, Aho-Corasick | The automaton that finds every term in one pass over the text, whatever the list size. |

### Two that are easy to confuse

- **Pack** is a *file format*. **Language pack** is one of the 14 optional
  lists. A language pack ships as a pack, and so does English.
- **Fold** is the act of normalising text. The **fold table** is the data that
  says how. The **fold profile** is the version of that data, and it is the one
  thing that makes a pack and a client incompatible.

---

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

The last row matters. `shell` stays clean, and **`a hell` is flagged** —
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
  vulgarity: ^0.1.0-pre.1
```

The version is a pre-release, and the constraint names `-pre.1` on purpose.
A plain `^0.1.0` matches no pre-release version, so it would find nothing to
install.

---

## Use it

The examples use mild terms on purpose. The bundled list runs to severity 5, and
`Score` rises with it.

```csharp
using Vulgarity;

// Build once and keep it. Compiling the trie costs far more than a scan.
var filter = VulgarityFilter.CreateDefault();

bool   dirty = filter.Detect("what the d.a.m.n");   // true
string clean = filter.Filter("what the d.a.m.n");   // "what the *******"
int    score = filter.Score("what the d.a.m.n");    // 1

foreach (VulgarityMatch m in filter.Scan("what the d.a.m.n"))
{
    // 9..16  "d.a.m.n"  ->  damn  (profanity, 1)
    Console.WriteLine($"{m.Start}..{m.End} {m.Excerpt(text)} -> {m.Text} {m.Severity}");
}
```

```dart
import 'package:vulgarity/vulgarity.dart';

final filter = VulgarityFilter.createDefault();

final dirty = filter.detect('what the d.a.m.n');   // true
final clean = filter.filter('what the d.a.m.n');   // what the *******
final score = filter.score('what the d.a.m.n');    // 1

for (final m in filter.scan('what the d.a.m.n')) {
  print('${m.start}..${m.end} ${m.excerpt(text)} -> ${m.text} ${m.severity}');
}
```

A `VulgarityFilter` holds no mutable state, so many threads can scan through one
instance at the same time.

### Offsets point at the original text

`Start` and `End` index the string you passed in, not the folded form. So you can
highlight the exact span, emoji and accents included.

```csharp
var hit = filter.Scan("café 🙂 d.a.m.n end")[0];
hit.Excerpt("café 🙂 d.a.m.n end");   // "d.a.m.n"
```

---

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `MinSeverity` | `1` | Ignore terms below this severity. Set `2` to drop clinical anatomy. |
| `Categories` | all | Restrict matching to a set of categories. |
| `MaskChar` | `*` | The character `Filter` repeats. |
| `MaskToken` | none | A fixed replacement string. It overrides `MaskChar`. |
| `RepeatTolerance` | `true` | Run the second scan that catches `daaamn`. |
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

## Presets: policy as data

A **seed** document carries terms. A **preset** carries the whole policy —
options, extra terms, an allowlist, and terms to drop. So a server can change
how strict a client is with no app release.

```json
{
  "schema": 1,
  "profile": "fold-v1",
  "name": "brand",
  "description": "What this policy is for.",

  "languages": ["en", "es"],

  "options": {
    "minSeverity": 2,
    "categories": ["hate", "violence"],
    "maskChar": "-",
    "maskToken": null,
    "repeatTolerance": true,
    "collapseContained": true,
    "scoreMode": "total"
  },

  "entries": [
    { "t": "blorpco", "cat": "profanity", "sev": 4, "w": true }
  ],
  "allow": ["blorpcoshire"],
  "remove": ["damn", "hell", "crap"]
}
```

Only `schema` and `profile` are checked. Every other field is optional.

| Field | Effect |
| --- | --- |
| `languages` | Which bundled lists to load. `"en"` is the curated English list. |
| `options` | The policy. A missing field keeps its default. |
| `entries` | Terms to add, in the seed entry shape. |
| `allow` | Innocent words the filter must never flag. |
| `remove` | Terms to drop after the lists load. An absent term is not an error. |

They apply in that order: **load, add, then take away.**

### Fetch one and use it

```csharp
string json = await http.GetStringAsync("https://example.com/policy.json");
var filter = VulgarityFilter.FromPreset(json);
```

```dart
final response = await http.get(Uri.parse('https://example.com/policy.json'));
final filter = VulgarityFilter.fromPreset(response.body);
```

Or build it up yourself:

```csharp
var filter = new VulgarityFilterBuilder()
    .AddPreset(json)
    .AddTerm("extra", "profanity", 3, requireBoundary: true)
    .RemoveTerm("damn")
    .Build();                       // the preset's options apply
```

```dart
final filter = (VulgarityFilterBuilder()
      ..addPreset(json)
      ..addTerm('extra', 'profanity', 3, true)
      ..removeTerm('damn'))
    .build();                       // the preset's options apply
```

Options you pass to `Build` win over the preset's. Pass none and the preset's
policy applies.

### One difference in Dart

.NET carries every language pack in the assembly, so `FromPreset` resolves
`languages` on its own. Dart keeps each pack in its own library so an unused one
stays out of your build, which means a preset naming any language but `en` needs
a resolver:

```dart
import 'package:vulgarity/languages.dart';   // pulls in all 14 packs

final filter = VulgarityFilter.fromPreset(json, languageResolver: languageSeed);
```

To keep an app small, import only the packs you need and resolve them yourself:

```dart
import 'package:vulgarity/lang/es.dart';

final filter = VulgarityFilter.fromPreset(
  json,
  languageResolver: (code) => switch (code) {
    'en' => kSeedEn,
    'es' => seedEs,
    _ => throw ArgumentError('unsupported language: $code'),
  },
);
```

A preset that names only `en` needs no resolver in either language.

### A preset from the network is untrusted

Parsing validates every field and throws with the offending field named. It
never applies a document in part.

```
"This build reads preset schema 1. The document states schema 99."
"This build implements fold profile 'fold-v1'. The preset states 'fold-v9'."
"'categories' names 'nonsense', which this build does not know. Valid names: ..."
"'scoreMode' must be 'total' or 'max'. It states 'sideways'."
"Severity must be 1 to 5. Term 'x' states 77."
```

Two fields differ on purpose:

- An unknown name in **`options.categories` fails**. Accepting it would silently
  match nothing, and a filter that quietly stops filtering is worse than one
  that stops.
- An unknown **term category is tolerated** and maps to `Other`, keeping its
  original name. So an older client still works when a server adds a category.

### Round-tripping

`VulgarityPreset` and `VulgarityOptions` both serialise, so a server can build a
policy with the same library that consumes it.

```csharp
string json = VulgarityPreset.Parse(source).ToJson();
VulgarityOptions again = VulgarityOptions.FromJson(options.ToJson());
```

### Four worked examples

`data/presets/` holds one preset per idea, and both test suites run every one
against a shared vector file:

| File | What it shows |
| --- | --- |
| `default.json` | The plain English list. Equal to `CreateDefault()`. |
| `strict.json` | A fixed mask token, and scoring by the worst single match. |
| `hate-only.json` | An escalation policy: severity 4 and up, slurs and threats only. |
| `brand.json` | Every feature at once — two languages, an added term, an allowlist entry, three terms dropped. |

---

## Packs: the term list without the words

A compiled binary used to carry the term list in plain text. `strings` on the
assembly printed 418 of the 526 English terms, and the Dart port shipped 1.29 MB
of readable JSON that pub.dev rendered on the package page. It no longer does.

The bundled lists ship as **packs**: the same terms in a compact record format,
XOR-masked with a fixed keystream. `strings` now finds nothing, and the payload
is 6.6 times smaller.

| | before | after |
| --- | ---: | ---: |
| Bundled bytes, all 15 lists | 1,281,491 | **194,198** |
| Dart source under `lib/` | 1,290,239 | **258,952** |
| English terms readable in the built assembly | **418** | **0** |

**This is obfuscation, not encryption.** The key ships beside the data in both
ports, and `data/*.json` is in this repository in plain text. Anyone who wants
the list can still have it. The goal is only that nobody meets it by accident —
not a reader of your package page, not a colleague running `strings`.

### Nothing changes for a caller

JSON stays the wire format. Every documented remote path works exactly as before:

```csharp
string json = await http.GetStringAsync("https://example.com/policy.json");
var filter = VulgarityFilter.FromPreset(json);        // unchanged
```

`AddSeed` reads the format from the input itself. It accepts three shapes:

| Shape | How it is recognised |
| --- | --- |
| A seed or preset document | the first character that is not blank is `{` |
| A pack, as base64 text | the text decodes and starts with `VPK1` |
| A pack, as raw bytes | `AddSeed(byte[])`, `addSeedBytes` |

Blank space and the URL-safe alphabet are both accepted, so `base64 < list.vpk`
and a pack pasted into a URL each work on both ports.

### Hosting your own list

Pack it, then serve the result in place of the JSON:

```bash
python3 tool/pack.py my-list.json -o my-list.vpk     # raw bytes
python3 tool/pack.py my-list.json --base64           # text, to stdout
python3 tool/pack.py my-list.vpk --show              # read one back as JSON
```

One caution: `VulgarityLanguages.ReadSeed` and Dart's `languageSeed` still
return a `String`, so every caller that only forwards the value keeps working.
The value is now pack text, not JSON. Code that ran `jsonDecode` on it must
call `AddSeed` instead.

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

| Text | Stream A | Stream B | `damn` | `ass` |
| --- | --- | --- | --- | --- |
| `damn` | `damn` | `damn` | hit in A | — |
| `daaamn` | `daaamn` | `damn` | hit in B | — |
| `ass` | `ass` | `as` | — | hit in A |
| `as` | `as` | `as` | — | **no hit** |

Stream A always wins. Stream B widens a span across the letters it collapsed, so
in `damn the music crap` it would report `c crap` for the second term. Stream A
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
| `a hell` | `hell` | a space was dropped | **flag** |
| `shell` | `hell` | `s` is a word character | clean |
| `d.a.m.n!` | `damn` | `!` folds soft, not hard | **flag** |

The allowlist only handles what this cannot. It holds **16 entries**, and a test
fails on any entry the matcher does not need. That test exists because an
innocent word that merely contains a term once sat in the allowlist, where it
also suppressed the correct match on the two-word phrase that folds to it. The
boundary rule already handled the innocent word, so the entry was doing harm
and no good.

---

## Repository layout

```
data/                     the contract. Neither language owns it.
  fold-v1.json            the fold table
  seed.json               the curated English list, authored and reviewable
  seed.<lang>.json        14 optional packs
  packs/seed-<code>.vpk   what actually ships: the same lists, masked
  presets/                four worked preset documents
  vectors.json            38 behavioural cases both ports must reproduce
  preset-vectors.json     44 preset cases both ports must reproduce
  fold-vectors.json       35 normalizer cases
  testdata/               the community corpus, used only by tests

tool/                     generators, all idempotent, all with --check
  gen_fold_table.py       fold-v1.json + the C# and Dart tables
  gen_seed.py             seed.json
  gen_lang_packs.py       seed.<lang>.json
  gen_packs.py            data/packs/*.vpk from data/*.json
  gen_dart_seeds.py       the Dart pack libraries
  packlib.py              the pack format: the encoder and the mask
  pack.py                 pack a list of your own, for hosting it yourself
  find_plain_terms.py     fails when a published file names a term
  foldlib.py              a Python reference fold, used by the tools

dotnet/src/Vulgarity/     the C# library
dotnet/tests/             311 tests
dart/lib/                 the Dart library
dart/test/                321 tests, not published to pub.dev
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
python3 tool/gen_packs.py --check
python3 tool/gen_dart_seeds.py --check

# No published file names a term
python3 tool/find_plain_terms.py --check

# Both ports agree, byte for byte
dotnet build dotnet/example/Example.csproj
dotnet dotnet/example/bin/Debug/net8.0/Example.dll "h3ll happens" > /tmp/a
cd dart && dart run example/vulgarity_example.dart "h3ll happens" > /tmp/b
diff /tmp/a /tmp/b
```

What the suites check:

- **Parity** — both ports read `data/vectors.json` and `data/preset-vectors.json`
  and must produce identical detect, scan, filter and score results.
- **The normalizer** — both ports read `data/fold-vectors.json` and must produce
  identical characters, offsets, `hard` flags and `gap` flags.
- **The fold table** — the compiled C# and Dart tables must equal
  `data/fold-v1.json`.
- **False positives** — both ports sweep the system word list (235,762 words).
  A word that only the squeeze pass flags fails the build, unless the allowlist
  covers it.
- **Evasion** — 1,013 real evasion spellings from the community corpus
  (`d4mn`, `h3ll`, `cr@p`, `.d a m n`) must all be detected.
- **The allowlist** — every entry must be one the matcher genuinely needs.
- **Language packs** — each pack loads, targets this profile, is marked
  unvetted, and finds every term it carries.
- **Presets** — every worked example builds, a malformed document is refused
  with a named field, and both preset and options round-trip through JSON.

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
- **A few slurs are ordinary words too.** The list holds several entries that
  are also everyday English in another sense, such as a narrow opening or an
  embankment. They stay in the list, because the slur is the common reading.
  Add them to your own allowlist if your domain needs the other one.
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
