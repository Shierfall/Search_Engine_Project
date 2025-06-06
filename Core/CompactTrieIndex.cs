using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SearchEngine.Analysis;
using SearchEngine.Core.Interfaces;
using System.Linq;
using System.Collections;

namespace SearchEngine.Core;

public class CompactTrieIndex : IExactPrefixIndex, IFullTextIndex
{
    public class TrieNode
    {
        public int PoolIndex, Offset, Length;
        public TrieNode[] ArrayChildren = new TrieNode[26];
        public Dictionary<char, TrieNode> DictChildren = new(2);
        public List<int> DocIds = new();
        public Dictionary<int, List<int>> Positions = new(); // maps docId to positions list
        public bool IsEndOfWord;

        public TrieNode() { }
        public TrieNode(int poolIndex, int offset, int length)
        {
            PoolIndex = poolIndex;
            Offset = offset;
            Length = length;
        }
    }

    private readonly TrieNode root = new();
    private readonly List<string> wordPool = new();
    private readonly Dictionary<string, int> wordToPoolIndex = new();
    
    // locks for thread safety
    private readonly ReaderWriterLockSlim _trielock = new ReaderWriterLockSlim();
    private readonly object _poolLock = new object();
    private readonly object _statsLock = new object();
    
    // helper property to check if read lock is held
    private bool IsReadLockHeld => _trielock.RecursionPolicy == LockRecursionPolicy.SupportsRecursion || 
                                  _trielock.CurrentReadCount > 0;
    
    // bit index for fast boolean search
    private readonly Dictionary<string, BitArray> _bitIndex = new(StringComparer.Ordinal);
    private bool _bitBuilt;
    private int _nextDocId;
    
    // BM25 specific fields
    private readonly Dictionary<int, int> _docLengths = new();
    private double _avgDocLength = 0;
    private int _totalDocs = 0;
    private double _k1 = 1.2; // BM25 parameter: term frequency saturation
    private double _b = 0.75; // BM25 parameter: document length normalization
    
    // toggle for delta encoding
    private bool _delta = true;
    
    // BM25 toggle
    private bool _useBM25 = true;

    public CompactTrieIndex() { }
    
    // methods to tune BM25 parameters
    public void SetBM25Params(double k1, double b)
    {
        if (k1 < 0) throw new ArgumentException("k1 must be non-negative");
        if (b < 0 || b > 1) throw new ArgumentException("b must be between 0 and 1");
        
        _k1 = k1;
        _b = b;
    }
    
    public (double k1, double b) GetBM25Params() => (_k1, _b);
    
    // toggle delta encoding on/off
    public void SetDeltaEncoding(bool on) => _delta = on;

    private int GetPoolIndex(string word)
    {
        lock (_poolLock)
        {
            if (!wordToPoolIndex.TryGetValue(word, out int idx))
            {
                idx = wordPool.Count;
                wordPool.Add(word);
                wordToPoolIndex[word] = idx;
            }
            return idx;
        }
    }

    private void Insert(TrieNode node, int poolIdx, int offset, int length, int docId, int position = -1)
    {
        if (length == 0)
        {
            MarkNode(node, docId, position);
            return;
        }
        char c = wordPool[poolIdx][offset];
        TrieNode child = null;
        int ci = c - 'a';
        if (ci >= 0 && ci < 26)
            child = node.ArrayChildren[ci];
        else
            node.DictChildren.TryGetValue(c, out child);

        if (child == null)
        {
            var newNode = new TrieNode(poolIdx, offset, length);
            newNode.IsEndOfWord = true;
            newNode.DocIds.Add(docId);
            
            // initialize positions for this document
            if (position >= 0)
            {
                newNode.Positions[docId] = new List<int> { position };
            }
            
            if (ci >= 0 && ci < 26)
                node.ArrayChildren[ci] = newNode;
            else
                node.DictChildren[c] = newNode;
            return;
        }

        int common = CommonPrefix(child, poolIdx, offset, length);
        if (common < child.Length)
        {
            // split child
            var splitNode = new TrieNode(child.PoolIndex,
                    child.Offset + common,
                    child.Length - common)
            {
                ArrayChildren = child.ArrayChildren,
                DictChildren = child.DictChildren,
                IsEndOfWord = child.IsEndOfWord,
                DocIds = child.DocIds,
                Positions = child.Positions
            };
            child.Length = common;
            child.IsEndOfWord = false;
            child.DocIds = new List<int>();
            child.Positions = new Dictionary<int, List<int>>();
            child.ArrayChildren = new TrieNode[26];
            child.DictChildren = new Dictionary<char, TrieNode>(2);
            char splitChar = wordPool[splitNode.PoolIndex][splitNode.Offset];
            int sci = splitChar - 'a';
            if (sci >= 0 && sci < 26)
                child.ArrayChildren[sci] = splitNode;
            else
                child.DictChildren[splitChar] = splitNode;
            if (common == length)
            {
                MarkNode(child, docId, position);
            }
            else
            {
                var newChild = new TrieNode(poolIdx, offset + common, length - common);
                newChild.IsEndOfWord = true;
                newChild.DocIds.Add(docId);
                
                // initialize positions for this document
                if (position >= 0)
                {
                    newChild.Positions[docId] = new List<int> { position };
                }
                
                char nc = wordPool[newChild.PoolIndex][newChild.Offset];
                int nci = nc - 'a';
                if (nci >= 0 && nci < 26)
                    child.ArrayChildren[nci] = newChild;
                else
                    child.DictChildren[nc] = newChild;
            }
            return;
        }

        Insert(child, poolIdx, offset + common, length - common, docId, position);
    }

    private void MarkNode(TrieNode node, int docId, int position = -1)
    {
        node.IsEndOfWord = true;
        if (!node.DocIds.Contains(docId))
            node.DocIds.Add(docId);
        // add position data if provided
        if (position >= 0)
        {
            if (node.Positions.TryGetValue(docId, out var positions))
            {
                int lastPos = positions[positions.Count - 1];
                positions.Add(_delta ? position - lastPos : position);
            }
            else
            {
                node.Positions[docId] = new List<int> { position };
            }
        }
    }

    private int CommonPrefix(TrieNode child, int poolIdx, int offset, int length)
    {
        int max = Math.Min(child.Length, length);
        var pool = wordPool;
        for (int i = 0; i < max; i++)
        {
            if (pool[child.PoolIndex][child.Offset + i] != pool[poolIdx][offset + i])
                return i;
        }
        return max;
    }

    public bool Search(string query)
    {
        _trielock.EnterReadLock();
        try
        {
            var node = FindNode(root, query.ToLowerInvariant());
            return node != null && node.IsEndOfWord;
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }

    private TrieNode FindNode(TrieNode node, string query)
    {
        int idx = 0;
        while (node != null && idx < query.Length)
        {
            char c = query[idx];
            TrieNode child = null;
            int ci = c - 'a';
            if (ci >= 0 && ci < 26)
                child = node.ArrayChildren[ci];
            else
                node.DictChildren.TryGetValue(c, out child);

            if (child == null) return null;
            int match = 0;
            while (match < child.Length && idx + match < query.Length &&
                    wordPool[child.PoolIndex][child.Offset + match] == query[idx + match])
                match++;
            if (match < child.Length) return null;
            idx += match;
            node = child;
        }
        return node;
    }

    public List<int> PrefixSearchDocuments(string prefix)
    {
        _trielock.EnterReadLock();
        try
        {
            prefix = prefix.ToLowerInvariant();
            var docs = new HashSet<int>();
            var node = FindNode(root, prefix);
            if (node != null)
            {
                CollectDocs(node, docs);
            }
            return new List<int>(docs);
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }

    private void CollectDocs(TrieNode node, HashSet<int> docs)
    {
        if (node.IsEndOfWord)
            foreach (var id in node.DocIds)
            {
                docs.Add(id);
            }
        foreach (var child in node.ArrayChildren)
        {
            if (child != null) CollectDocs(child, docs);
        }
        foreach (var kv in node.DictChildren)
        {
            CollectDocs(kv.Value, docs);
        }
    }

    public List<(string word, List<int> docIds)> PrefixSearch(string prefix)
    {
        _trielock.EnterReadLock();
        try
        {
            prefix = prefix.ToLowerInvariant();
            var node = FindNode(root, prefix);
            if (node == null) return new List<(string, List<int>)>();

            var builder = new List<(string, List<int>)>();
            var currentWord = prefix;
            CollectWords(node, currentWord, builder);
            return builder;
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }

    private void CollectWords(TrieNode node, string prefix, List<(string, List<int>)> output)
    {
        if (node.IsEndOfWord)
        {
            output.Add((prefix, new List<int>(node.DocIds)));
        }
        foreach (var c in node.ArrayChildren)
        {
            if (c != null)
            {
                var w = wordPool[c.PoolIndex].Substring(c.Offset, c.Length);
                CollectWords(c, prefix + w, output);
            }
        }
        foreach (var kv in node.DictChildren)
        {
            var c = kv.Value;
            var w = wordPool[c.PoolIndex].Substring(c.Offset, c.Length);
            CollectWords(c, prefix + w, output);
        }
    }

    public void AddDocument(int docId, IEnumerable<Token> tokens)
    {
        var tokenList = tokens.ToList();
        int docLength = tokenList.Count;
        
        // update document length stats for BM25
        lock (_statsLock)
        {
            if (_docLengths.ContainsKey(docId))
            {
                // if we are updating a document, first remove its old length from the average
                _avgDocLength = (_avgDocLength * _totalDocs - _docLengths[docId]) / (_totalDocs - 1);
                _docLengths[docId] = docLength;
            }
            else
            {
                _docLengths[docId] = docLength;
                _totalDocs++;
            }
            
            // recalculate average document length
            _avgDocLength = _docLengths.Values.Sum() / (double)_totalDocs;
            
            if (docId >= _nextDocId)
            {
                _nextDocId = docId + 1;
            }
        }

        // process each token with write lock
        _trielock.EnterWriteLock();
        try
        {
            foreach (var token in tokenList.Where(t => !string.IsNullOrEmpty(t.Term)))
            {
                int poolIdx = GetPoolIndex(token.Term);
                Insert(root, poolIdx, 0, token.Term.Length, docId, token.Position);
            }
            
            // mark bit index as needing rebuild
            _bitBuilt = false;
        }
        finally
        {
            _trielock.ExitWriteLock();
        }
    }

    // add batch document processing
    public void AddDocumentsBatch(IEnumerable<(int docId, IEnumerable<Token> tokens)> documents)
    {
        var docTerms = new ConcurrentDictionary<int, List<Token>>();
        var docLengthsMap = new ConcurrentDictionary<int, int>();
        
        // first process tokens in parallel to extract terms and positions per document
        Parallel.ForEach(documents, doc => 
        {
            var tokenList = doc.tokens.ToList();
            docTerms[doc.docId] = tokenList;
            docLengthsMap[doc.docId] = tokenList.Count; // track document length for BM25
        });
        
        // update document length stats for BM25
        lock (_statsLock)
        {
            foreach (var entry in docLengthsMap)
            {
                int docId = entry.Key;
                int docLength = entry.Value;
                
                if (_docLengths.ContainsKey(docId))
                {
                    // if we are updating a document, first remove its old length from the average
                    _avgDocLength = (_avgDocLength * _totalDocs - _docLengths[docId]) / (_totalDocs - 1);
                    _docLengths[docId] = docLength;
                }
                else
                {
                    _docLengths[docId] = docLength;
                    _totalDocs++;
                }
                
                if (docId >= _nextDocId)
                {
                    _nextDocId = docId + 1;
                }
            }
            
            // recalculate average document length
            _avgDocLength = _docLengths.Values.Sum() / (double)_totalDocs;
        }
        
        // then prepare word pool indices (to minimize lock contention during trie updates)
        var termPoolIndices = new ConcurrentDictionary<string, int>();
        foreach (var docEntry in docTerms)
        {
            foreach (var token in docEntry.Value.Where(t => !string.IsNullOrEmpty(t.Term)))
            {
                if (!termPoolIndices.ContainsKey(token.Term))
                {
                    int poolIdx = GetPoolIndex(token.Term);
                    termPoolIndices[token.Term] = poolIdx;
                }
            }
        }
        
        // then update trie with single write lock (to maintain trie integrity)
        _trielock.EnterWriteLock();
        try
        {
            foreach (var docEntry in docTerms)
            {
                int docId = docEntry.Key;
                foreach (var token in docEntry.Value.Where(t => !string.IsNullOrEmpty(t.Term)))
                {
                    int poolIdx = termPoolIndices[token.Term];
                    Insert(root, poolIdx, 0, token.Term.Length, docId, token.Position);
                }
            }
            
            // mark bit index as needing rebuild
            _bitBuilt = false;
        }
        finally
        {
            _trielock.ExitWriteLock();
        }
    }

    public void RemoveDocument(int docId, IEnumerable<Token> tokens)
    {
        // update document length stats for BM25
        lock (_statsLock)
        {
            if (_docLengths.ContainsKey(docId))
            {
                // remove document length from average calculation
                if (_totalDocs > 1)
                {
                    _avgDocLength = (_avgDocLength * _totalDocs - _docLengths[docId]) / (_totalDocs - 1);
                }
                else
                {
                    _avgDocLength = 0;
                }
                
                _docLengths.Remove(docId);
                _totalDocs--;
            }
        }
        
        _trielock.EnterWriteLock();
        try
        {
            foreach (var token in tokens)
            {
                if (string.IsNullOrEmpty(token.Term)) continue;
                
                // check if the word exists in the pool
                if (wordToPoolIndex.TryGetValue(token.Term, out int poolIdx))
                {
                    RemoveWord(root, poolIdx, 0, token.Term.Length, docId);
                }
            }
            
            // mark bit index as needing rebuild
            _bitBuilt = false;
        }
        finally
        {
            _trielock.ExitWriteLock();
        }
    }

    private bool RemoveWord(TrieNode node, int poolIndex, int offset, int length, int docId)
    {
        if (length == 0)
        {
            // reached the node, remove docId
            node.DocIds.Remove(docId);
            node.Positions.Remove(docId);
            if (node.DocIds.Count == 0)
            {
                node.IsEndOfWord = false;
            }
        }
        else
        {
            char c = wordPool[poolIndex][offset];
            TrieNode child = null;
            int ci = c - 'a';
            if (ci >= 0 && ci < 26) child = node.ArrayChildren[ci];
            else node.DictChildren.TryGetValue(c, out child);

            if (child == null) return false;

            int common = CommonPrefix(child, poolIndex, offset, length);
            if (common < child.Length)
            {
                // the word to remove is a prefix of an edge, so it's not in the trie.
                return false;
            }

            // recurse
            if (RemoveWord(child, poolIndex, offset + common, length - common, docId))
            {
                // prune child if it's now empty
                if (ci >= 0 && ci < 26) node.ArrayChildren[ci] = null;
                else node.DictChildren.Remove(c);
            }
        }
        
        // check if current node can be pruned
        if (!node.IsEndOfWord)
        {
            bool hasChildren = false;
            foreach (var c in node.ArrayChildren) {
                if (c != null) 
                {
                    hasChildren = true;
                    break;
                }
            }
            if (!hasChildren) hasChildren = node.DictChildren.Count > 0;

            if (!hasChildren) return true; // prune this node
        }

        return false;
    }

    public void Clear()
    {
        _trielock.EnterWriteLock();
        try
        {
            // clear all children of root
            Array.Clear(root.ArrayChildren, 0, root.ArrayChildren.Length);
            root.DictChildren.Clear();
            root.DocIds.Clear();
            root.Positions.Clear();
            root.IsEndOfWord = false;
            
            // clear the word pool
            wordPool.Clear();
            wordToPoolIndex.Clear();
            
            // clear BM25 data
            lock (_statsLock)
            {
                _docLengths.Clear();
                _avgDocLength = 0;
                _totalDocs = 0;
                _nextDocId = 0;
            }
            
            // clear bit index
            _bitIndex.Clear();
            _bitBuilt = false;
        }
        finally
        {
            _trielock.ExitWriteLock();
        }
    }

    public List<(int docId, int count)> ExactSearchDocuments(string term)
    {
        _trielock.EnterReadLock();
        try
        {
            term = term.ToLowerInvariant();
            var node = FindNode(root, term);
            if (node == null || !node.IsEndOfWord) 
                return new List<(int docId, int count)>();
                
            // convert to required format with count always set to 1
            return node.DocIds.Select(id => (id, 1)).ToList();
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }

    public List<(int docId, int count)> ExactSearch(string searchStr)
    {
        _trielock.EnterReadLock();
        try
        {
            searchStr = searchStr.ToLowerInvariant();
            var node = FindNode(root, searchStr);
            if (node == null || !node.IsEndOfWord) 
                return new List<(int docId, int count)>();
                
            // calculate BM25 scores
            var results = new List<(int docId, double score)>(node.DocIds.Count);
            
            foreach (var docId in node.DocIds)
            {
                int termFreq = node.Positions.TryGetValue(docId, out var positions) ? positions.Count : 1;
                double score = CalculateBM25Score(searchStr, docId, termFreq);
                results.Add((docId, score));
            }

            // format results with scores converted to counts
            var finalResults = new List<(int docId, int count)>(results.Count);
            foreach (var item in results.OrderByDescending(r => r.score))
            {
                finalResults.Add((item.docId, (int)(item.score * 1000))); // scaled score
            }
            
            return finalResults;
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }
    
    public List<(int docId, int count)> PhraseSearch(string phrase)
{
    _trielock.EnterReadLock();
    try
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new List<(int docId, int count)>();

        if (words.Length == 1) return ExactSearch(words[0]);

        // find the first term
        var firstNode = FindNode(root, words[0].ToLowerInvariant());
        if (firstNode == null || !firstNode.IsEndOfWord) 
            return new List<(int docId, int count)>();

        // initialize candidates with positions from first term
        var candidates = new Dictionary<int, List<int>>();
        
        foreach (var docId in firstNode.DocIds)
        {
            if (firstNode.Positions.TryGetValue(docId, out var positions))
            {
                candidates[docId] = positions.Count > 0 && positions[0] == 0 ? 
                    Decode(positions) : new List<int>(positions);
            }
        }
        
        // process remaining terms
        for (int i = 1; i < words.Length; i++)
        {
            var nextNode = FindNode(root, words[i].ToLowerInvariant());
            if (nextNode == null || !nextNode.IsEndOfWord) 
                return new List<(int docId, int count)>();
                
            var nextSet = new Dictionary<int, List<int>>();
            
            foreach (var docId in nextNode.DocIds)
            {
                if (!candidates.TryGetValue(docId, out var prevPositions)) continue;
                
                if (nextNode.Positions.TryGetValue(docId, out var currPositions))
                {
                    var valid = MergePositions(prevPositions, 
                        currPositions.Count > 0 && currPositions[0] == 0 ? 
                        Decode(currPositions) : currPositions);
                        
                    if (valid.Count > 0)
                    {
                        nextSet[docId] = valid;
                    }
                }
            }
            
            candidates = nextSet;
            if (candidates.Count == 0) break;
        }
        
        var docIds = candidates.Keys.ToList();
        var results = new List<(int docId, double score)>(docIds.Count);
        
        foreach (var docId in docIds)
        {
            double totalScore = 0;
            for (int i = 0; i < words.Length; i++)
            {
                var node = FindNode(root, words[i].ToLowerInvariant());
                if (node != null && node.IsEndOfWord && node.DocIds.Contains(docId))
                {
                    int termFreq = node.Positions.TryGetValue(docId, out var positions) ? positions.Count : 1;
                    totalScore += CalculateBM25Score(words[i], docId, termFreq);
                }
            }
            //boost
            totalScore *= 1.2;
            results.Add((docId, totalScore));
        }
        var finalResults = new List<(int docId, int count)>(results.Count);
        foreach (var item in results.OrderByDescending(r => r.score))
        {
            finalResults.Add((item.docId, (int)(item.score * 1000))); // scaled
        }
        
        return finalResults;
    }
    finally
    {
        _trielock.ExitReadLock();
    }
}

    public List<(int docId, int count)> BooleanSearchNaive(string expr)
    {
        _trielock.EnterReadLock();
        try
        {
            var terms = expr.Split(new[] { "&&", "||" }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(t => t.Trim().ToLowerInvariant())
                           .Where(t => !string.IsNullOrWhiteSpace(t))
                           .ToList();

            var operators = new List<string>();
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

            var docSets = new List<HashSet<int>>();
            foreach (var term in terms)
            {
                var node = FindNode(root, term);
                if (node != null && node.IsEndOfWord)
                {
                    docSets.Add(new HashSet<int>(node.DocIds));
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

            // calculate scores for matching documents
            var scoreResults = new List<(int docId, double score)>(result.Count);
            foreach (var docId in result)
            {
                double totalScore = 0;
                foreach (var term in terms)
                {
                    var node = FindNode(root, term);
                    if (node != null && node.IsEndOfWord && node.DocIds.Contains(docId))
                    {
                        int termFreq = node.Positions.TryGetValue(docId, out var positions) ? positions.Count : 1;
                        totalScore += CalculateBM25Score(term, docId, termFreq);
                    }
                }
                scoreResults.Add((docId, totalScore));
            }

            // prepare final results
            var finalResults = new List<(int docId, int count)>(scoreResults.Count);
            foreach (var item in scoreResults.OrderByDescending(r => r.score))
            {
                finalResults.Add((item.docId, (int)(item.score * 1000))); // scaled
            }
            
            return finalResults;
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }

    private void BuildBits()
    {
        if (_bitBuilt) return;
        _bitIndex.Clear();
        
        bool needsLock = !IsReadLockHeld;
        if (needsLock)
            _trielock.EnterReadLock();
        
        try
        {
            foreach (var word in wordToPoolIndex.Keys)
            {
                var node = FindNode(root, word.ToLowerInvariant());
                if (node != null && node.IsEndOfWord)
                {
                    var bits = new BitArray(_nextDocId);
                    foreach (var docId in node.DocIds)
                    {
                        if (docId < _nextDocId)
                            bits[docId] = true;
                    }
                    _bitIndex[word] = bits;
                }
            }
            _bitBuilt = true;
        }
        finally
        {
            if (needsLock)
                _trielock.ExitReadLock();
        }
    }
    
    public List<(int docId, int count)> BooleanSearch(string expr)
    {
        _trielock.EnterReadLock();
        try
        {
            BuildBits();

            BitArray? acc = null;
            string? op = null;
            var terms = new List<string>();
            
            foreach (var token in expr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token is "&&" or "||") { op = token; continue; }
                
                terms.Add(token.ToLowerInvariant());
                _bitIndex.TryGetValue(token.ToLowerInvariant(), out var bits);
                bits ??= new BitArray(_nextDocId); // all false

                acc = acc == null
                    ? (BitArray)bits.Clone()
                    : op == "&&" ? acc.And(bits)
                    : op == "||" ? acc.Or(bits)
                    : acc;
                op = null;
            }
            if (acc == null) return new List<(int docId, int count)>();

            // collect matching doc IDs
            var docIds = new List<int>();
            for (int i = 0; i < acc.Length; i++)
                if (acc[i]) docIds.Add(i);
            
            if (docIds.Count == 0) return new List<(int docId, int count)>();
            
            // calculate BM25 scores for each matching document
            var results = new List<(int docId, double score)>(docIds.Count);
            foreach (var docId in docIds)
            {
                double totalScore = 0;
                foreach (var term in terms)
                {
                    var node = FindNode(root, term);
                    if (node != null && node.IsEndOfWord && node.DocIds.Contains(docId))
                    {
                        int termFreq = node.Positions.TryGetValue(docId, out var positions) ? positions.Count : 1;
                        totalScore += CalculateBM25Score(term, docId, termFreq);
                    }
                }
                
                results.Add((docId, totalScore));
            }
            
            // preallocation of final list with exact capacity
            var finalResults = new List<(int docId, int count)>(results.Count);
            foreach (var item in results.OrderByDescending(r => r.score))
            {
                finalResults.Add((item.docId, (int)(item.score * 1000))); // scaled
            }
            
            return finalResults;
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }
    
    // BM25 scoring function
    private double CalculateBM25Score(string term, int docId, int termFrequency)
    {
        if (!_useBM25) return termFrequency; // if BM25 is disabled, just return term frequency
        
        var node = FindNode(root, term.ToLowerInvariant());
        if (node == null || !node.IsEndOfWord || !node.DocIds.Contains(docId) || 
            !_docLengths.TryGetValue(docId, out var docLength))
            return 0;
            
        // IDF component: log((N-n+0.5)/(n+0.5))
        double N = _totalDocs;
        double n = node.DocIds.Count; // number of documents containing the term
        double idf = Math.Log((N - n + 0.5) / (n + 0.5) + 1.0);
        
        // normalized term frequency
        double normalizedTF = termFrequency / (1.0 - _b + _b * (docLength / _avgDocLength));
        
        // BM25 score
        double score = idf * (normalizedTF * (_k1 + 1)) / (normalizedTF + _k1);
        
        return score;
    }
    
    // helper functions for working with positions
    private static List<int> Decode(List<int> deltas)
    {
        var res = new List<int>(deltas.Count);
        int cur = 0;
        foreach (var d in deltas) 
        { 
            cur += d; 
            res.Add(cur); 
        }
        return res;
    }
    
    private static List<int> MergePositions(List<int> prev, List<int> cur)
    {
        var outp = new List<int>();
        
        // delta decoder if needed
        if (prev.Count > 0 && prev[0] == 0) prev = Decode(prev);
        if (cur.Count > 0 && cur[0] == 0) cur = Decode(cur);
        
        int i = 0, j = 0;
        while (i < prev.Count && j < cur.Count)
        {
            int target = prev[i] + 1;
            while (j < cur.Count && cur[j] < target)
            {
                j++;
            }
            if (j < cur.Count && cur[j] == target)
            {
                outp.Add(cur[j]);
            }
            i++;
        }
        
        return outp;
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

    public int TotalNodeCount {
        get {
            int count = 0;
            foreach (var node in GetAllNodes()) {
                count++;
            }
            return count;
        }
    }

    public IEnumerable<TrieNode> GetAllNodes()
    {
        // breadth-first traversal
        if (root == null) yield break;
        
        var queue = new Queue<TrieNode>();
        queue.Enqueue(root);
        
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;
            for (int i = 0; i < 26; i++)
            {
                if (node.ArrayChildren != null && node.ArrayChildren[i] != null)
                {
                    queue.Enqueue(node.ArrayChildren[i]);
                }
            }
            if (node.DictChildren != null)
            {
                foreach (var child in node.DictChildren.Values)
                {
                    if (child != null)
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }
    }

    public List<string> WordPool => wordPool;

    public Dictionary<string, int> WordToPoolIndex => wordToPoolIndex;

    public int GetMaxDocIdForKey(string key)
    {
        if (_bitIndex.TryGetValue(key, out var bitArray))
        {
            for (int i = bitArray.Length - 1; i >= 0; i--)
            {
                if (bitArray[i]) return i;
            }
        }
        return -1;
    }

    public int DocumentCount => _totalDocs;

    public void SetBM25Enabled(bool enabled)
    {
        _useBM25 = enabled;
    }

    public bool IsBM25Enabled() => _useBM25;
}
