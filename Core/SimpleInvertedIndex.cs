using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SearchEngine.Analysis;
using SearchEngine.Core.Interfaces;

namespace SearchEngine.Core;

public sealed class SimpleInvertedIndex : IFullTextIndex
{
    // ----- posting list ------------------------------------------------------
    private sealed class Posting
    {
        public int DocId;
        public int Count;
        public readonly List<int> Positions;

        public Posting(int docId, int pos)
        {
            DocId = docId;
            Count = 1;
            Positions = new() { pos };
        }
    }

    // ----- fields ------------------------------------------------------------
    private readonly Dictionary<string, List<Posting>> _map = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BitArray> _bitIndex = new(StringComparer.Ordinal);

    private bool _bitBuilt;
    private int _nextDocId;
    private bool _delta = true;

    public void SetDeltaEncoding(bool on) => _delta = on;

    // ------------------------------------------------------------------------
    public void AddDocument(int docId, IEnumerable<Token> tokens)
    {
        foreach (var t in tokens)
        {
            if (string.IsNullOrEmpty(t.Term)) continue;

            if (!_map.TryGetValue(t.Term, out var list))
                _map[t.Term] = list = new();

            if (list.Count > 0 && list[^1].DocId == docId)
            {
                int last = list[^1].Positions[^1];
                list[^1].Positions.Add(_delta ? t.Position - last : t.Position);
                list[^1].Count++;
            }
            else
            {
                list.Add(new Posting(docId, t.Position));
            }
        }
        _bitBuilt = false;

        if (docId >= _nextDocId)
        {
            _nextDocId = docId + 1;
        }
    }

    public void RemoveDocument(int docId, IEnumerable<Token> tokens)
    {
        // remove document from all term postings
        foreach (var t in tokens)
        {
            if (!_map.TryGetValue(t.Term, out var list)) continue;
            list.RemoveAll(p => p.DocId == docId);
            if (list.Count == 0) _map.Remove(t.Term);
        }
        _bitBuilt = false;
    }

    public void Clear()
    {
        _map.Clear();           // remove all postings
        _bitIndex.Clear();      // remove all bit-vectors
        _nextDocId = 0;         // reset doc-id counter
        _bitBuilt = false;      // mark bits as needing rebuild
    }

    // ------------------------------------------------------------------------
    public List<(int docId, int count)> ExactSearch(string searchStr)
    {
        if (!_map.TryGetValue(searchStr, out var postings))
        {
            return new List<(int docId, int count)>();
        }

        var result = new List<(int docId, int count)>(postings.Count);

        foreach (var posting in postings)
        {
            result.Add((posting.DocId, posting.Count));
        }

        return result;
    }

    public List<(int docId, int count)> PhraseSearch(string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new List<(int docId, int count)>();

        if (words.Length == 1) return ExactSearch(words[0]);

        if (!_map.TryGetValue(words[0], out var first)) return new List<(int docId, int count)>();

        var candidates = first.ToDictionary(p => p.DocId, p => new List<int>(p.Positions));
        var matchCounts = new Dictionary<int, int>();

        for (int i = 1; i < words.Length; i++)
        {
            if (!_map.TryGetValue(words[i], out var next)) return new List<(int docId, int count)>();
            var nextSet = new Dictionary<int, List<int>>();
            var nextMatchCounts = new Dictionary<int, int>();

            foreach (var p in next)
            {
                if (!candidates.TryGetValue(p.DocId, out var prev)) continue;
                var valid = MergePositions(prev, p.Positions);
                if (valid.Count > 0)
                {
                    nextSet[p.DocId] = valid;
                    // Track match count
                    nextMatchCounts[p.DocId] = matchCounts.TryGetValue(p.DocId, out var prevCount) ?
                        prevCount + 1 : 1;
                }
            }
            candidates = nextSet;
            matchCounts = nextMatchCounts;
            if (candidates.Count == 0) return new List<(int docId, int count)>();
        }

        // preallocation results list
        var candidateKeys = candidates.Keys.ToList();
        var results = new List<(int docId, int count)>(candidateKeys.Count);

        // manually build sorted results instead of using LINQ
        // this simulates OrderByDescending but with better performance
        var orderedIds = new List<(int docId, int count)>(candidateKeys.Count);
        foreach (var id in candidateKeys)
        {
            var count = matchCounts.TryGetValue(id, out var c) ? c : 0;
            orderedIds.Add((id, count));
        }

        // simple insertion sort (faster than LINQ OrderByDescending for small lists)
        orderedIds.Sort((a, b) => b.count.CompareTo(a.count));

        return orderedIds;
    }

    public List<(int docId, int count)> BooleanSearch(string expr)
    {
        BuildBits();

        BitArray? acc = null;
        string? op = null;

        foreach (var token in expr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "&&" or "||") { op = token; continue; }

            _bitIndex.TryGetValue(token, out var bits);
            bits ??= new BitArray(_nextDocId); // all false

            acc = acc == null
                ? (BitArray)bits.Clone()
                : op == "&&" ? acc.And(bits)
                : op == "||" ? acc.Or(bits)
                : acc;
            op = null;
        }
        if (acc == null) return new List<(int docId, int count)>();

        // estimate size for pre-allocation
        int estimatedMatches = 0;
        for (int i = 0; i < acc.Length; i++)
            if (acc[i]) estimatedMatches++;

        var result = new List<(int docId, int count)>(estimatedMatches);
        for (int i = 0; i < acc.Length; i++)
        {
            if (acc[i]) result.Add((i, 1));
        }

        return result;
    }

    private void BuildBits()
    {
        if (_bitBuilt) return;
        _bitIndex.Clear();

        foreach (var (term, postings) in _map)
        {
            var bits = new BitArray(_nextDocId);
            foreach (var p in postings) bits[p.DocId] = true;
            _bitIndex[term] = bits;
        }
        _bitBuilt = true;
    }

    private static int BinarySearch(List<int> list, int value)
    {
        int left = 0, right = list.Count - 1;
        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            if (list[mid] == value) return mid;
            if (list[mid] < value) left = mid + 1;
            else right = mid - 1;
        }
        return -1;
    }

    private static List<int> MergePositions(List<int> prev, List<int> cur)
    {
        var outp = new List<int>();
        // delta decoder
        if (prev.Count > 0 && prev[0] == 0) prev = Decode(prev);
        if (cur.Count > 0 && cur[0] == 0) cur = Decode(cur);

        // use binary search for each prev position
        foreach (var p in prev)
        {
            int idx = BinarySearch(cur, p + 1);
            if (idx != -1)
                outp.Add(cur[idx]);
        }
        return outp;
    }

    private static List<int> Decode(List<int> deltas)
    {
        var res = new List<int>(deltas.Count);
        int cur = 0;
        foreach (var d in deltas) { cur += d; res.Add(cur); }
        return res;
    }

    public List<(string word, List<int> docIds)> PrefixSearch(string prefix)
    {
        // estimate capacity to avoid resizing
        var estimatedMatches = Math.Min(20, _map.Count / 10); // rough guess
        var results = new List<(string word, List<int> docIds)>(estimatedMatches);

        foreach (var kvp in _map)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // pre-allocate docIds list
                var docIds = new List<int>(kvp.Value.Count);
                foreach (var posting in kvp.Value)
                {
                    docIds.Add(posting.DocId);
                }
                results.Add((kvp.Key, docIds));
            }
        }
        return results;
    }

    public List<(int docId, int count)> BooleanSearchNaive(string expr)
    {
        var terms = expr.Split(new[] { "&&", "||" }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.Trim())
                       .Where(t => !string.IsNullOrWhiteSpace(t))
                       .ToList();

        // preallocation operators list with estimated capacity
        var operators = new List<string>(terms.Count - 1);
        int i = 0;
        while (i < expr.Length - 1)
        {
            if (i + 2 <= expr.Length && expr.Substring(i, 2) == "&&")
            {
                operators.Add("&&");
                i += 2;
            }
            else if (i + 2 <= expr.Length && expr.Substring(i, 2) == "||")
            {
                operators.Add("||");
                i += 2;
            }
            else
            {
                i++;
            }
        }

        // preallocate with known capacity
        var docSets = new List<HashSet<int>>(terms.Count);
        foreach (var term in terms)
        {
            if (_map.TryGetValue(term, out var postings))
            {
                var docSet = new HashSet<int>(postings.Count);
                foreach (var posting in postings)
                {
                    docSet.Add(posting.DocId);
                }
                docSets.Add(docSet);
            }
            else
            {
                docSets.Add(new HashSet<int>());
            }
        }

        if (docSets.Count == 0) return new List<(int docId, int count)>();

        var result = docSets[0];
        for (int j = 0; j < operators.Count; j++)
        {
            if (operators[j] == "&&")
            {
                result.IntersectWith(docSets[j + 1]);
            }
            else // "||"
            {
                result.UnionWith(docSets[j + 1]);
            }
        }

        // preallocate result with exact capacity
        var finalResults = new List<(int docId, int count)>(result.Count);
        foreach (var docId in result)
        {
            finalResults.Add((docId, 1));
        }

        return finalResults;
    }

    public void AddDocumentsBatch(IEnumerable<(int docId, IEnumerable<Token> tokens)> documents)
    {
        throw new NotImplementedException();
    }
}
