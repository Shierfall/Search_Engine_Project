using System;
using System.Collections.Generic;
using System.IO;

namespace SearchEngineProject;
internal class Index
{
    private TrieNode root;
    private int counter;

    private class TrieNode
    {
        public TrieNode[] Children;
        public DocumentLog Log;
        public int Count;
        public bool isEndOfWord;

        public TrieNode()
        {
            Children = new TrieNode[36];
            Count = 0;
            isEndOfWord = false;
        }
    }

    private class DocumentLog
    {
        public string Title;
        public DocumentLog Next;
        public DocumentLog(string title, DocumentLog next)
        {
            Title = title;
            Next = next;
        }
    }

    public Index(string filename)
    {
        root = new TrieNode();
        try
        {
            using (StreamReader input = new StreamReader(filename, System.Text.Encoding.UTF8))
            {
                string line;
                string currentTitle = "";
                bool titleRead = false;
                while ((line = input.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    if (line.Equals("---END.OF.DOCUMENT---"))
                    {
                        titleRead = false;
                        currentTitle = "";
                        continue;
                    }
                    if (!titleRead)
                    {
                        currentTitle = line;
                        // Console.WriteLine(currentTitle);
                        titleRead = true;
                    }
                    else
                    {
                        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (string word in words)
                        {
                            InsertWord(word, currentTitle);
                        }
                    }
                }
            }
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine("Error reading file " + filename);
        }
    }

    private void InsertWord(string word, string title)
    {
        word = word.ToLower();
        TrieNode current = root;
        foreach (char c in word)
        {
            // FIX: Support special characters, currently only supports letters and numbers
            if (char.IsLetterOrDigit(c))
            {
                int index;
                if (char.IsLetter(c))
                {
                    index = c - 'a'; // letters, 'a' -> 0, 'b' -> 1
                }
                else // if it's a digit
                {
                    index = 26 + (c - '0'); // digits, '0' -> 26, '1' -> 27
                }
                if (index >= 0 && index < current.Children.Length)
                {
                    if (current.Children[index] == null)
                    {
                        current.Children[index] = new TrieNode();
                    }
                    current = current.Children[index];
                }
            }
        }
        current.isEndOfWord = true;
        current.Count++;
        if (current.Log == null)
        {
            current.Log = new DocumentLog(title, null);
        }
        else if (!DocExists(current.Log, title))
        {
            current.Log = new DocumentLog(title, current.Log);
        }
    }

    private TrieNode FindWord(string word)
    {
        word = word.ToLower();
        TrieNode current = root;
        foreach (char c in word)
        {
            int index = -1;
            if (char.IsLetter(c))
            {
                index = c - 'a';
            }
            else if (char.IsDigit(c))
            {
                index = 26 + (c - '0');
            }
            else
            {
                // skip any special character for now
                continue;
            }
            if (current.Children[index] == null)
                return null;
            current = current.Children[index];
        }
        return current.isEndOfWord ? current : null;
    }


    private bool DocExists(DocumentLog log, string title)
    {
        while (log != null)
        {
            if (log.Title.Equals(title))
                return true;
            log = log.Next;
        }
        return false;
    }

    // NOTE: exact match search (unchanged in terms of implementation compared to like iondex4)
    public bool Search(string searchStr)
    {
        TrieNode current = FindWord(searchStr);
        if (current != null)
        {
            Console.WriteLine("Found " + searchStr + " in:");
            DocumentLog log = current.Log;
            while (log != null)
            {
                Console.WriteLine(log.Title);
                log = log.Next;
            }
            return true;
        }
        else
        {
            Console.WriteLine("No matches for " + searchStr + " found");
            return false;
        }
    }

    // NOTE: auto-completion, lists all words starting with the given prefix and their documents
    public void PrefixSearch(string prefix)
    {
        prefix = prefix.ToLower();
        TrieNode node = FindPrefixNode(prefix);
        if (node == null)
        {
            Console.WriteLine("No matches for " + prefix + " found");
            return;
        }

        List<(string word, TrieNode node)> completions = new List<(string, TrieNode)>();
        CollectWords(node, prefix, completions);

        if (completions.Count == 0)
        {
            Console.WriteLine("No complete words found with prefix " + prefix);
        }
        else
        {
            foreach (var (word, trieNode) in completions)
            {
                Console.WriteLine("Found " + word + " in:");
                DocumentLog log = trieNode.Log;
                while (log != null)
                {
                    Console.WriteLine(log.Title);
                    log = log.Next;
                }
            }
        }
    }

    // NOTE: returns the (unique) documents  where any word starting with the given prefix appears
    public void PrefixSearchDocuments(string prefix)
    {
        prefix = prefix.ToLower();
        TrieNode node = FindPrefixNode(prefix);
        if (node == null)
        {
            Console.WriteLine("No matches for " + prefix + " found");
            return;
        }

        // hashset to avoid duplicate titles
        HashSet<string> documents = new HashSet<string>();

        List<(string word, TrieNode node)> completions = new List<(string, TrieNode)>();
        CollectWords(node, prefix, completions);

        foreach (var (word, trieNode) in completions)
        {
            DocumentLog log = trieNode.Log;
            while (log != null)
            {
                documents.Add(log.Title);
                log = log.Next;
            }
        }

        if (documents.Count == 0)
        {
            Console.WriteLine($"No documents found for prefix: '{prefix}'");
        }
        else
        {
            Console.WriteLine($"Documents containing a word starting with '{prefix}'");

            foreach (var doc in documents)
            {
                Console.WriteLine(doc);
            }
        }
    }

    /*
     * helper to locate node corresponding to the last character of the prefix
     */
    private TrieNode FindPrefixNode(string prefix)
    {
        TrieNode current = root;
        foreach (char c in prefix)
        {
            int index = -1;
            if (char.IsLetter(c))
            {
                index = c - 'a';
            }
            else if (char.IsDigit(c))
            {
                index = 26 + (c - '0');
            }
            else
            {
                //  ignore special characters for now
                continue;
            }
            if (current.Children[index] == null)
                return null;
            current = current.Children[index];
        }
        return current;
    }

    // recursively collects complete words (nodes marked as isEndOfWord) from a given node
    //  iterates over the entire Children array 
    private void CollectWords(TrieNode node, string currentPrefix, List<(string, TrieNode)> completions)
    {
        if (node == null)
            return;

        if (node.isEndOfWord)
            completions.Add((currentPrefix, node));

        for (int i = 0; i < node.Children.Length; i++)
        {
            if (node.Children[i] != null)
            {
                char nextChar;
                if (i < 26)
                {
                    nextChar = (char)(i + 'a');
                }
                else
                {
                    nextChar = (char)(i - 26 + '0');
                }
                CollectWords(node.Children[i], currentPrefix + nextChar, completions);
            }
        }
    }
}

