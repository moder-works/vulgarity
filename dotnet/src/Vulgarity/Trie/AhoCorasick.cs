using System;
using System.Collections.Generic;
using Vulgarity.Normalization;

namespace Vulgarity.Trie
{
    /// <summary>One raw hit from a scan.</summary>
    internal struct RawHit
    {
        /// <summary>The pattern that matched.</summary>
        public int PatternId;

        /// <summary>The index in the scanned text of the pattern's last character.</summary>
        public int End;

        public RawHit(int patternId, int end)
        {
            PatternId = patternId;
            End = end;
        }
    }

    /// <summary>
    /// A prefix trie with failure links. It finds every pattern in one pass over
    /// the text, so the cost stays linear in the length of the text.
    /// </summary>
    internal sealed class AhoCorasick
    {
        private readonly List<Dictionary<int, int>> _children = new List<Dictionary<int, int>>();
        private readonly List<int> _lengths = new List<int>();
        private readonly List<int> _terminal = new List<int>();
        private int[] _fail;
        private int[] _outputLink;
        private bool _built;

        public AhoCorasick()
        {
            _children.Add(new Dictionary<int, int>());
            _terminal.Add(-1);
        }

        /// <summary>How many patterns the trie holds.</summary>
        public int PatternCount
        {
            get { return _lengths.Count; }
        }

        /// <summary>The character count of one pattern.</summary>
        public int LengthOf(int patternId)
        {
            return _lengths[patternId];
        }

        /// <summary>Inserts one pattern.</summary>
        /// <returns>
        /// The id of the pattern. A pattern that is already present keeps its
        /// first id, so the trie never holds a duplicate.
        /// </returns>
        public int Add(int[] pattern)
        {
            if (_built)
            {
                throw new InvalidOperationException("Add a pattern before you call Build.");
            }

            if (pattern.Length == 0)
            {
                throw new ArgumentException("A pattern must hold at least one character.", "pattern");
            }

            int node = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                int next;
                if (!_children[node].TryGetValue(pattern[i], out next))
                {
                    next = _children.Count;
                    _children.Add(new Dictionary<int, int>());
                    _terminal.Add(-1);
                    _children[node][pattern[i]] = next;
                }

                node = next;
            }

            if (_terminal[node] >= 0)
            {
                return _terminal[node];
            }

            int id = _lengths.Count;
            _lengths.Add(pattern.Length);
            _terminal[node] = id;
            return id;
        }

        /// <summary>Computes the failure links. Call this once, after the last Add.</summary>
        public void Build()
        {
            if (_built)
            {
                return;
            }

            int count = _children.Count;
            _fail = new int[count];
            _outputLink = new int[count];
            for (int i = 0; i < count; i++)
            {
                _outputLink[i] = -1;
            }

            Queue<int> queue = new Queue<int>();

            // Depth 1 always fails back to the root.
            foreach (KeyValuePair<int, int> edge in _children[0])
            {
                _fail[edge.Value] = 0;
                queue.Enqueue(edge.Value);
            }

            while (queue.Count > 0)
            {
                int node = queue.Dequeue();

                int failNode = _fail[node];
                _outputLink[node] = _terminal[failNode] >= 0 ? failNode : _outputLink[failNode];

                foreach (KeyValuePair<int, int> edge in _children[node])
                {
                    int character = edge.Key;
                    int child = edge.Value;

                    int candidate = _fail[node];
                    int target;
                    while (candidate != 0 && !_children[candidate].ContainsKey(character))
                    {
                        candidate = _fail[candidate];
                    }

                    if (_children[candidate].TryGetValue(character, out target) && target != child)
                    {
                        _fail[child] = target;
                    }
                    else
                    {
                        _fail[child] = 0;
                    }

                    queue.Enqueue(child);
                }
            }

            _built = true;
        }

        /// <summary>Finds every pattern in the text and appends each hit to <paramref name="hits"/>.</summary>
        public void Scan(NormalizedText text, List<RawHit> hits)
        {
            if (!_built)
            {
                throw new InvalidOperationException("Call Build before you scan.");
            }

            if (_lengths.Count == 0)
            {
                return;
            }

            int node = 0;
            int[] chars = text.Chars;

            for (int i = 0; i < chars.Length; i++)
            {
                int character = chars[i];

                while (node != 0 && !_children[node].ContainsKey(character))
                {
                    node = _fail[node];
                }

                int next;
                node = _children[node].TryGetValue(character, out next) ? next : 0;

                int output = _terminal[node] >= 0 ? node : _outputLink[node];
                while (output >= 0)
                {
                    hits.Add(new RawHit(_terminal[output], i));
                    output = _outputLink[output];
                }
            }
        }
    }
}
