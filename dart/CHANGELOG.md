# Changelog

## 0.1.0

The first release.

- Trie-based detection, filtering and scoring, on an Aho-Corasick automaton.
  `detect` stops at the first hit; `scan` returns every match ordered by start;
  `filter` merges overlapping matches so no character is masked twice.
- A normalizer that folds text to profile `fold-v1` before it matches. It
  defeats case, leetspeak, separators, repeated letters, accents, homoglyphs
  and zero-width characters. The fold is idempotent: folding a folded string
  changes nothing.
- A word-boundary rule that counts a dropped separator as a boundary, so
  `a hell` is flagged while `shell` is not — the same letters, told apart by
  the gap between the words. A term that needs no boundary but swallows a
  separator *inside* the match must still begin its own word, so an ordinary
  pair of words whose tail and head happen to join into a term stays clean.
  The right edge stays free, so a term written with a space or a hyphen
  between every letter still matches. A 16-word allowlist covers what the
  rules cannot, such as `Scunthorpe` and `shiitake`.
- A second pass with repeated letters collapsed, so `daaamn` reaches `damn`.
  It scans a second trie holding the squeezed spelling of every term, and a
  term that actually lost a letter must land on runs at least as long as its
  own — so a doubled-letter term cannot stand in for an ordinary word that
  shares its squeezed spelling. The pass is a fallback, not a second opinion:
  a candidate that lands on a span the first pass already found is dropped, so
  the reported span stays tight. Turn it off with `repeatTolerance: false`.
- Every match carries offsets into the ORIGINAL text. `excerpt` returns the
  span as it was written, and `term.text` the folded list entry it reached.
- A hand-curated English list of 526 terms, with a category and a severity of 1
  to 5 on every entry.
- Fourteen optional language packs, each its own library, so an unimported pack
  stays out of your build. All are community-sourced and unvetted.
- `VulgarityOptions` for severity, categories, masking, repeat tolerance,
  containment and scoring mode. It is `const`-constructible, and `withOptions`
  swaps a policy without recompiling the trie.
- `VulgarityPreset`, so a server can change a client's policy with no app
  release. `VulgarityPreset.parse` is the one place an untrusted document is
  read: it checks the fields in a fixed order, names the one it rejects, and
  reports every fault as a `FormatException`. `addPreset` then stages the whole
  policy before it commits any of it, so a language that cannot be resolved or
  a term that folds to nothing leaves the builder exactly as it was.
- Who may widen a term is settled by where it came from. A term list, or a
  term named in code through `addTerm`, is a source the app author chose, so it
  may drop a boundary. The `entries` of a preset are the one source that can
  arrive from the network, so there the boundary is sticky and the severity may
  only rise: a remote policy can make a bundled term stricter, never looser.
  A preset that names a language other than `en` needs a `LanguageResolver`,
  so an unimported pack stays out of your build. An unknown option category is
  refused, an unknown term category is tolerated as `other`, and removing a
  term the list never held is not an error — both so that an older client keeps
  working against a newer policy.
- The bundled lists ship as masked packs, so a compiled app carries no readable
  term. `addSeed` reads the format from the input itself, so a JSON document
  works just as well.
- A matching .NET package reads the same lists and the same test vectors. Both
  ports are checked against each other, byte for byte, in CI.
