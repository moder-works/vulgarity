# Changelog

## 0.1.0

The first release.

- Trie-based detection, filtering and scoring, on an Aho-Corasick automaton.
- A normalizer that folds text to profile `fold-v1` before it matches. It
  defeats case, leetspeak, separators, repeated letters, accents, homoglyphs
  and zero-width characters.
- A word-boundary rule, plus a 16-word allowlist, to keep ordinary words such
  as `Scunthorpe`, `assassin` and `shell` clean.
- A hand-curated English list of 526 terms, with a category and a severity of 1
  to 5 on every entry.
- Fourteen optional language packs, each its own library, so an unimported pack
  stays out of your build. All are community-sourced and unvetted.
- `VulgarityOptions` for severity, categories, masking, repeat tolerance,
  containment and scoring mode.
- `VulgarityPreset`, so a server can change a client's policy with no app
  release.
- The bundled lists ship as masked packs, so a compiled app carries no readable
  term. `addSeed` reads the format from the input itself, so a JSON document
  still works exactly as before.
- A matching .NET package reads the same lists and the same test vectors. Both
  ports are checked against each other, byte for byte, in CI.
