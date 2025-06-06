using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Linq;
using SearchEngine.Analysis;
using SearchEngine.Core.Interfaces;

namespace SearchEngine.Core;

/// <summary>
/// A simplified trie-based index that only implements IExactPrefixIndex interface.
/// Removes all full-text search features like BM25, phrase search, boolean search,
/// and position tracking to provide a lightweight autocomplete/prefix search implementation.
/// </summary>
public class SimpleTrieIndex : IExactPrefixIndex
{
    public class TrieNode
    {
        public int PoolIndex, Offset, Length;
        public TrieNode[] ArrayChildren = new TrieNode[26];
        public Dictionary<char, TrieNode> DictChildren = new(2);
        public Dictionary<int, int> DocCounts = new(); // maps docId to term count in that document
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

    public SimpleTrieIndex() { }

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

    private void Insert(TrieNode node, int poolIdx, int offset, int length, int docId)
    {
        if (length == 0)
        {
            MarkNode(node, docId);
            return;
        }

        var word = wordPool[poolIdx];
        var c = word[offset];

        TrieNode child;
        if (c >= 'a' && c <= 'z')
        {
            child = node.ArrayChildren[c - 'a'];
            if (child == null)
            {
                child = new TrieNode(poolIdx, offset + 1, length - 1);
                node.ArrayChildren[c - 'a'] = child;
            }
        }
        else
        {
            if (!node.DictChildren.TryGetValue(c, out child))
            {
                child = new TrieNode(poolIdx, offset + 1, length - 1);
                node.DictChildren[c] = child;
            }
        }

        Insert(child, poolIdx, offset + 1, length - 1, docId);
    }

    private void MarkNode(TrieNode node, int docId)
    {
        node.IsEndOfWord = true;
        node.DocCounts[docId] = node.DocCounts.GetValueOrDefault(docId, 0) + 1;
    }

    private bool Remove(TrieNode node, int poolIdx, int offset, int length, int docId)
    {
        if (length == 0)
        {
            UnmarkNode(node, docId);
        }
        else
        {
            var word = wordPool[poolIdx];
            var c = word[offset];
            TrieNode child = null;
            int childArrIndex = -1;

            if (c >= 'a' && c <= 'z')
            {
                childArrIndex = c - 'a';
                child = node.ArrayChildren[childArrIndex];
            }
            else
            {
                node.DictChildren.TryGetValue(c, out child);
            }

            if (child != null)
            {
                if (Remove(child, poolIdx, offset + 1, length - 1, docId))
                {
                    if (childArrIndex != -1)
                    {
                        node.ArrayChildren[childArrIndex] = null;
                    }
                    else
                    {
                        node.DictChildren.Remove(c);
                    }
                }
            }
        }

        if (!node.IsEndOfWord)
        {
            foreach (var child in node.ArrayChildren)
            {
                if (child != null) return false;
            }
            if (node.DictChildren.Count > 0) return false;

            return true;
        }

        return false;
    }

    private void UnmarkNode(TrieNode node, int docId)
    {
        if (node.DocCounts.ContainsKey(docId))
        {
            node.DocCounts[docId]--;
            if (node.DocCounts[docId] <= 0)
            {
                node.DocCounts.Remove(docId);
                if (node.DocCounts.Count == 0)
                {
                    node.IsEndOfWord = false;
                }
            }
        }
    }

    public void AddDocument(int docId, IEnumerable<Token> tokens)
    {
        _trielock.EnterWriteLock();
        try
        {
            foreach (var token in tokens)
            {
                var normalizedTerm = token.Term.ToLowerInvariant();
                var poolIdx = GetPoolIndex(normalizedTerm);
                Insert(root, poolIdx, 0, normalizedTerm.Length, docId);
            }
        }
        finally
        {
            _trielock.ExitWriteLock();
        }
    }

    public void RemoveDocument(int docId, IEnumerable<Token> tokens)
    {
        _trielock.EnterWriteLock();
        try
        {
            foreach (var token in tokens)
            {
                var normalizedTerm = token.Term.ToLowerInvariant();
                if (wordToPoolIndex.TryGetValue(normalizedTerm, out int poolIdx))
                {
                    Remove(root, poolIdx, 0, normalizedTerm.Length, docId);
                }
            }
        }
        finally
        {
            _trielock.ExitWriteLock();
        }
    }

    public void AddDocumentsBatch(IEnumerable<(int docId, IEnumerable<Token> tokens)> documents)
    {
        _trielock.EnterWriteLock();
        try
        {
            foreach (var (docId, tokens) in documents)
            {
                foreach (var token in tokens)
                {
                    var normalizedTerm = token.Term.ToLowerInvariant();
                    var poolIdx = GetPoolIndex(normalizedTerm);
                    Insert(root, poolIdx, 0, normalizedTerm.Length, docId);
                }
            }
        }
        finally
        {
            _trielock.ExitWriteLock();
        }
    }

    public bool Search(string term)
    {
        var normalizedTerm = term.ToLowerInvariant();
        _trielock.EnterReadLock();
        try
        {
            var node = FindNode(root, normalizedTerm, 0);
            return node?.IsEndOfWord == true;
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }

    public List<int> PrefixSearchDocuments(string prefix)
    {
        var normalizedPrefix = prefix.ToLowerInvariant();
        _trielock.EnterReadLock();
        try
        {
            var prefixNode = FindNode(root, normalizedPrefix, 0);
            if (prefixNode == null)
                return new List<int>();

            var docIds = new HashSet<int>();
            CollectDocIds(prefixNode, docIds);
            return docIds.ToList();
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }

    public List<(string word, List<int> docIds)> PrefixSearch(string prefix)
    {
        var normalizedPrefix = prefix.ToLowerInvariant();
        _trielock.EnterReadLock();
        try
        {
            var prefixNode = FindNode(root, normalizedPrefix, 0);
            if (prefixNode == null)
                return new List<(string, List<int>)>();

            var results = new List<(string, List<int>)>();
            CollectWords(prefixNode, normalizedPrefix, results);
            return results;
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }

    public List<(int docId, int count)> ExactSearchDocuments(string term)
    {
        var normalizedTerm = term.ToLowerInvariant();
        _trielock.EnterReadLock();
        try
        {
            var node = FindNode(root, normalizedTerm, 0);
            if (node?.IsEndOfWord != true)
                return new List<(int, int)>();

            return node.DocCounts.Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }
        finally
        {
            _trielock.ExitReadLock();
        }
    }

    public List<(int docId, int count)> BooleanSearchNaive(string expr)
    {
        // simple implementation: treat as AND operation for multiple terms
        var terms = expr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
            return new List<(int, int)>();

        if (terms.Length == 1)
            return ExactSearchDocuments(terms[0]);

        // find intersection of all terms
        var termResults = terms.Select(term => ExactSearchDocuments(term))
                               .Where(results => results.Count > 0)
                               .ToList();

        if (termResults.Count == 0)
            return new List<(int, int)>();

        // start with first term results
        var intersection = termResults[0].ToDictionary(x => x.docId, x => x.count);

        // intersect with remaining terms
        for (int i = 1; i < termResults.Count; i++)
        {
            var currentTermDocs = termResults[i].ToDictionary(x => x.docId, x => x.count);
            var keysToRemove = intersection.Keys.Where(docId => !currentTermDocs.ContainsKey(docId)).ToList();
            
            foreach (var key in keysToRemove)
            {
                intersection.Remove(key);
            }

            // update counts (sum for boolean AND)
            foreach (var docId in intersection.Keys.ToList())
            {
                intersection[docId] += currentTermDocs[docId];
            }
        }

        return intersection.Select(kvp => (kvp.Key, kvp.Value)).ToList();
    }

    public void Clear()
    {
        _trielock.EnterWriteLock();
        try
        {
            // clear the trie structure
            ClearNode(root);
            
            // clear the word pool
            lock (_poolLock)
            {
                wordPool.Clear();
                wordToPoolIndex.Clear();
            }
        }
        finally
        {
            _trielock.ExitWriteLock();
        }
    }

    private void ClearNode(TrieNode node)
    {
        node.IsEndOfWord = false;
        node.DocCounts.Clear();
        
        // clear array children
        for (int i = 0; i < node.ArrayChildren.Length; i++)
        {
            if (node.ArrayChildren[i] != null)
            {
                ClearNode(node.ArrayChildren[i]);
                node.ArrayChildren[i] = null;
            }
        }
        
        // clear dictionary children
        foreach (var child in node.DictChildren.Values)
        {
            ClearNode(child);
        }
        node.DictChildren.Clear();
    }

    private TrieNode FindNode(TrieNode node, string str, int index)
    {
        if (index == str.Length)
            return node;

        var c = str[index];
        TrieNode child = null;

        if (c >= 'a' && c <= 'z')
        {
            child = node.ArrayChildren[c - 'a'];
        }
        else
        {
            node.DictChildren.TryGetValue(c, out child);
        }

        return child == null ? null : FindNode(child, str, index + 1);
    }

    private void CollectDocIds(TrieNode node, HashSet<int> docIds)
    {
        if (node.IsEndOfWord)
        {
            foreach (var docId in node.DocCounts.Keys)
            {
                docIds.Add(docId);
            }
        }

        // traverse array children
        foreach (var child in node.ArrayChildren)
        {
            if (child != null)
                CollectDocIds(child, docIds);
        }

        // traverse dictionary children
        foreach (var child in node.DictChildren.Values)
        {
            CollectDocIds(child, docIds);
        }
    }

    private void CollectWords(TrieNode node, string prefix, List<(string, List<int>)> results)
    {
        if (node.IsEndOfWord && node.DocCounts.Count > 0)
        {
            results.Add((prefix, node.DocCounts.Keys.ToList()));
        }

        // traverse array children
        for (int i = 0; i < node.ArrayChildren.Length; i++)
        {
            if (node.ArrayChildren[i] != null)
            {
                char c = (char)('a' + i);
                CollectWords(node.ArrayChildren[i], prefix + c, results);
            }
        }

        // traverse dictionary children
        foreach (var kvp in node.DictChildren)
        {
            CollectWords(kvp.Value, prefix + kvp.Key, results);
        }
    }
}
