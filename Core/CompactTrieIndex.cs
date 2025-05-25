using System;
using System.Collections.Generic;
using SearchEngine.Analysis;
using SearchEngine.Core.Interfaces;
using System.Linq;

namespace SearchEngine.Core;

public class CompactTrieIndex : IExactPrefixIndex
{
    private class TrieNode
    {
        public int PoolIndex, Offset, Length;
        public TrieNode[] ArrayChildren = new TrieNode[26];
        public Dictionary<char, TrieNode> DictChildren = new(2);
        public List<int> DocIds = new();
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

    public CompactTrieIndex() { }

    // public CompactTrieIndex(string filename)
    // {
    //     try
    //     {
    //         using var input = new StreamReader(filename, System.Text.Encoding.UTF8);
    //         string line;
    //         string currentTitle = null;
    //         bool titleRead = false;
    //         while ((line = input.ReadLine()) != null)
    //         {
    //             if (string.IsNullOrWhiteSpace(line)) continue;
    //             if (line.Equals("---END.OF.DOCUMENT---", StringComparison.Ordinal))
    //             {
    //                 titleRead = false;
    //                 currentTitle = null;
    //                 continue;
    //             }
    //             if (!titleRead)
    //             {
    //                 currentTitle = line;
    //                 RegisterTitle(currentTitle);
    //                 titleRead = true;
    //             }
    //             else
    //             {
    //                 foreach (var rawWord in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    //                 {
    //                     var word = Normalize(rawWord);
    //                     if (word.Length > 0)
    //                         InsertWord(word, currentTitle);
    //                 }
    //             }
    //         }
    //     }
    //     catch (FileNotFoundException)
    //     {
    //         throw;
    //     }
    // }

    // private void RegisterTitle(int docId, string title)
    // {
    //     while (idToTitle.Count <= docId)
    //     {
    //         idToTitle.Add(null);
    //     }
    //     idToTitle[docId] = title;
    // }
    //
    // private void InsertWord(string word, string title)
    // {
    //     int poolIdx = GetPoolIndex(word);
    //     int titleId = titleToId[title];
    //     Insert(root, poolIdx, 0, word.Length, titleId);
    // }

    private int GetPoolIndex(string word)
    {
        if (!wordToPoolIndex.TryGetValue(word, out int idx))
        {
            idx = wordPool.Count;
            wordPool.Add(word);
            wordToPoolIndex[word] = idx;
        }
        return idx;
    }

    private void Insert(TrieNode node, int poolIdx, int offset, int length, int titleId)
    {
        if (length == 0)
        {
            MarkNode(node, titleId);
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
            newNode.DocIds.Add(titleId);
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
                DocIds = child.DocIds
            };
            child.Length = common;
            child.IsEndOfWord = false;
            child.DocIds = new List<int>();
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
                MarkNode(child, titleId);
            }
            else
            {
                var newChild = new TrieNode(poolIdx, offset + common, length - common);
                newChild.IsEndOfWord = true;
                newChild.DocIds.Add(titleId);
                char nc = wordPool[newChild.PoolIndex][newChild.Offset];
                int nci = nc - 'a';
                if (nci >= 0 && nci < 26)
                    child.ArrayChildren[nci] = newChild;
                else
                    child.DictChildren[nc] = newChild;
            }
            return;
        }

        Insert(child, poolIdx, offset + common, length - common, titleId);
    }

    private void MarkNode(TrieNode node, int titleId)
    {
        node.IsEndOfWord = true;
        if (!node.DocIds.Contains(titleId))
            node.DocIds.Add(titleId);
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
        var node = FindNode(root, query.ToLowerInvariant());
        return node != null && node.IsEndOfWord;
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
        prefix.ToLowerInvariant(); // maybe ew can remove this since we do it in tokenizer
        var docs = new HashSet<int>();
        var node = FindNode(root, prefix);
        if (node != null)
        {
            CollectDocs(node, docs);
        }
        return new List<int>(docs);
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
        prefix = prefix.ToLowerInvariant(); 
        var results = new List<(string, List<int>)>();
        
        // Empty prefix should return limited set of all words
        if (string.IsNullOrWhiteSpace(prefix))
        {
            CollectAllWords(root, "", results, 100); // Limit to 100 words for empty prefix
            return results;
        }
        
        var startNode = FindNode(root, prefix);
        if (startNode != null)
        {
            CollectWords(startNode, prefix, results);
        }
        
        // if no exact matches, try partial matching
        if (results.Count == 0 && prefix.Length > 1)
        {
            // try partial matching by getting most common prefix
            var partialPrefix = prefix.Substring(0, prefix.Length - 1);
            var partialNode = FindNode(root, partialPrefix);
            if (partialNode != null)
            {
                CollectWords(partialNode, partialPrefix, results);
                
                // filter results to only include those that start with our partial prefix
                results = results
                    .Where(r => r.Item1.StartsWith(partialPrefix))
                    .ToList();
            }
        }
        
        return results;
    }

    // Helper method to collect all words in the trie (used when prefix is empty)
    private void CollectAllWords(TrieNode node, string current, List<(string, List<int>)> output, int limit)
    {
        if (output.Count >= limit)
            return;
            
        if (node.IsEndOfWord)
        {
            output.Add((current, new List<int>(node.DocIds)));
        }
        
        foreach (var c in node.ArrayChildren)
        {
            if (c != null && output.Count < limit)
            {
                var w = wordPool[c.PoolIndex].Substring(c.Offset, c.Length);
                CollectAllWords(c, current + w, output, limit);
            }
        }
        
        foreach (var kv in node.DictChildren)
        {
            if (output.Count < limit)
            {
                CollectAllWords(kv.Value, current + kv.Key, output, limit);
            }
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
            CollectWords(kv.Value, prefix + kv.Key, output);
        }
    }
    // helper for RemoveDocument
    ///<summary>
    ///if (length == 0)
    /// we're at the "terminal" node for this word.
    /// remove docId and unmark eow if empty.
    ///else
    /// compute c = next character
    /// look up child = node.ArrayChildren[...] or DictChildren[c]
    /// recurse: bool pruneChild = RemoveWord(child, poolIdx, offset+1, length-1, docId);
    /// if pruneChild==true, unlink that child pointer
    ///finally, check: no docs & no children ⇒ return true (prune this node too)
    ///<summary/>
    private bool RemoveWord(TrieNode node, int poolIndex, int offset, int length, int docId)
    {
        if (length == 0)
        {
            node.DocIds.Remove(docId);
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
            if (ci >= 0 && ci < 26)
            {
                child = node.ArrayChildren[ci];
            }
            else
            {
                node.DictChildren.TryGetValue(c, out child);
            }
            if (child == null)
            {
                return false;
            }

            bool prune = RemoveWord(child, poolIndex, offset + 1, length - 1, docId);
            if (prune)
            {
                if (ci >= 0 && ci < 26)
                {
                    node.ArrayChildren[ci] = null;
                }
                else
                {
                    node.DictChildren.Remove(c);
                }
            }
        }
        bool hasNoDocs = !node.IsEndOfWord;
        bool hasNoChildren = true;
        foreach (var child in node.ArrayChildren)
        {
            if (child != null)
            {
                hasNoChildren = false;
                break;
            }
        }

        if (hasNoChildren && node.DictChildren.Count > 0)
        {
            hasNoChildren = false;
        }

        return hasNoDocs && hasNoChildren;
    }
    public void AddDocument(int docId, IEnumerable<Token> tokens)
    {
        foreach (var tok in tokens)
        {
            var word = tok.Term;
            if (string.IsNullOrEmpty(word))
            {
                continue;
            }

            // we pool the word, then insert into the tree

            int poolIndex = GetPoolIndex(word);
            Insert(root, poolIndex, 0, word.Length, docId);
        }
    }

    public void RemoveDocument(int docId, IEnumerable<Token> tokens)
    {
        foreach (var tok in tokens)
        {
            var word = tok.Term;
            if (string.IsNullOrEmpty(word))
            {
                continue;
            }

            // find the final node for this word then remove docId from that node and if 
            // no docs remaining, then unmark the word
            //
            //
            if (!wordToPoolIndex.TryGetValue(word, out var poolIndex))
            {
                continue;
            }
            RemoveWord(root, poolIndex, 0, word.Length, docId);
        }
    }

    // private string Normalize(string raw)
    // {
    //     return raw.Trim().ToLowerInvariant();
    // }
    public void PrintDemo(string term, string prefix)
    {
        Console.WriteLine($"Search(\"{term}\") → {Search(term)}");

        var docsByPrefix = PrefixSearchDocuments(prefix);
        Console.WriteLine($"\nDocuments containing words starting with \"{prefix}\":");
        foreach (var title in docsByPrefix)
            Console.WriteLine($"  • {title}");

        var detailed = PrefixSearch(prefix);
        Console.WriteLine($"\nAll words beginning with \"{prefix}\" and their documents:");
        foreach (var (word, titles) in detailed)
        {
            Console.WriteLine($"  {word}:");
            foreach (var t in titles)
                Console.WriteLine($"    – {t}");
        }
    }
    public void Clear()
    {
        // for debugging, maybe for implementation
        // reset the root node
        for (int i = 0; i < 26; i++)
            root.ArrayChildren[i] = null;
        root.DictChildren.Clear();
        root.DocIds.Clear();
        root.IsEndOfWord = false;
        // reset pool of stored words
        wordPool.Clear();
        wordToPoolIndex.Clear();
    }

    public List<(int docId, int count)> BooleanSearchNaive(string expr)
    {
        var terms = expr.Split(new[] { "&&", "||" }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.Trim())
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
            var node = FindNode(root, term.ToLowerInvariant());
            if (node != null && node.IsEndOfWord)
            {
                docSets.Add(new HashSet<int>(node.DocIds));
            }
            else
            {
                docSets.Add(new HashSet<int>());
            }
        }

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

        return result.Select(id => (id, 1)).ToList();
    }

    public List<(int docId, int count)> ExactSearchDocuments(string term)
    {
        var node = FindNode(root, term.ToLowerInvariant());
        if (node == null || !node.IsEndOfWord)
        {
            return new List<(int docId, int count)>();
        }
        
        // Since the trie doesn't track counts per document like inverted index,
        // we'll just return 1 as the count for each document
        return node.DocIds
            .Select(docId => (docId, 1))
            .ToList();
    }
}
