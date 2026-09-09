import '../normalization/normalized_text.dart';

/// One raw hit from a scan.
class RawHit {
  const RawHit(this.patternId, this.end);

  /// The pattern that matched.
  final int patternId;

  /// The index in the scanned text of the pattern's last character.
  final int end;
}

/// A prefix trie with failure links.
///
/// It finds every pattern in one pass over the text, so the cost stays linear
/// in the length of the text.
class AhoCorasick {
  AhoCorasick() {
    _children.add(<int, int>{});
    _terminal.add(-1);
  }

  final List<Map<int, int>> _children = <Map<int, int>>[];
  final List<int> _lengths = <int>[];
  final List<int> _terminal = <int>[];
  late List<int> _fail;
  late List<int> _outputLink;
  bool _built = false;

  /// How many patterns the trie holds.
  int get patternCount => _lengths.length;

  /// The character count of one pattern.
  int lengthOf(int patternId) => _lengths[patternId];

  /// Inserts one pattern.
  ///
  /// Returns the id of the pattern. A pattern that is already present keeps its
  /// first id, so the trie never holds a duplicate.
  int add(List<int> pattern) {
    if (_built) {
      throw StateError('Add a pattern before you call build.');
    }
    if (pattern.isEmpty) {
      throw ArgumentError.value(
          pattern, 'pattern', 'A pattern must hold at least one character.');
    }

    int node = 0;
    for (final int c in pattern) {
      int? next = _children[node][c];
      if (next == null) {
        next = _children.length;
        _children.add(<int, int>{});
        _terminal.add(-1);
        _children[node][c] = next;
      }
      node = next;
    }

    if (_terminal[node] >= 0) {
      return _terminal[node];
    }

    final int id = _lengths.length;
    _lengths.add(pattern.length);
    _terminal[node] = id;
    return id;
  }

  /// Computes the failure links. Call this once, after the last [add].
  void build() {
    if (_built) {
      return;
    }

    final int count = _children.length;
    _fail = List<int>.filled(count, 0);
    _outputLink = List<int>.filled(count, -1);

    final List<int> queue = <int>[];
    int head = 0;

    // Depth 1 always fails back to the root.
    for (final int child in _children[0].values) {
      _fail[child] = 0;
      queue.add(child);
    }

    while (head < queue.length) {
      final int node = queue[head++];

      final int failNode = _fail[node];
      _outputLink[node] =
          _terminal[failNode] >= 0 ? failNode : _outputLink[failNode];

      _children[node].forEach((int character, int child) {
        int candidate = _fail[node];
        while (candidate != 0 && !_children[candidate].containsKey(character)) {
          candidate = _fail[candidate];
        }

        final int? target = _children[candidate][character];
        _fail[child] = (target != null && target != child) ? target : 0;
        queue.add(child);
      });
    }

    _built = true;
  }

  /// Finds every pattern in the text and appends each hit to [hits].
  void scan(NormalizedText text, List<RawHit> hits) {
    if (!_built) {
      throw StateError('Call build before you scan.');
    }
    if (_lengths.isEmpty) {
      return;
    }

    int node = 0;
    final List<int> chars = text.chars;

    for (int i = 0; i < chars.length; i++) {
      final int character = chars[i];

      while (node != 0 && !_children[node].containsKey(character)) {
        node = _fail[node];
      }

      node = _children[node][character] ?? 0;

      int output = _terminal[node] >= 0 ? node : _outputLink[node];
      while (output >= 0) {
        hits.add(RawHit(_terminal[output], i));
        output = _outputLink[output];
      }
    }
  }
}
