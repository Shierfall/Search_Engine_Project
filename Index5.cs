using System;
using System.Diagnostics;
using System.IO;

namespace SearchEngineProject
{
    internal class Index5
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
                Children = new TrieNode[26];
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

        public Index5(string filename)
        {
            //Stopwatch sw = new Stopwatch();
            //sw.Start();
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
                            Console.WriteLine(currentTitle);
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
                    //sw.Stop();
                    //Console.WriteLine("Indexing took " + sw.Elapsed);
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
                if (char.IsLetter(c))
                {
                    int index = c - 'a';
                    if (index >= 0 && index < 26)
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
                int index = c - 'a';
                if (current.Children[index] == null)
                {
                    return null;
                }
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

        private TrieNode FindPrefix(string prefix)
        {
            prefix = prefix.ToLower();
            TrieNode current = root;
            foreach (char c in prefix)
            {
                int index = c - 'a';
                if (index < 0 || index >= 26 || current.Children[index] == null)
                {
                    return null;
                }
                current = current.Children[index];
            }
            return current;
        }

        private DocumentLog PrintAllFromPrefix(TrieNode node, DocumentLog visitedDocs)
        {
            if (node == null) return visitedDocs;
            if (node.isEndOfWord)
            {
                DocumentLog log = node.Log;
                while (log != null)
                {
                    if (!DocExists(visitedDocs, log.Title))
                    {
                        Console.WriteLine(log.Title);
                        visitedDocs = new DocumentLog(log.Title, visitedDocs);
                    }
                    log = log.Next;
                }
            }
            for (int i = 0; i < 26; i++)
            {
                if (node.Children[i] != null)
                    visitedDocs = PrintAllFromPrefix(node.Children[i], visitedDocs);
            }
            return visitedDocs;
        }

        public bool Search(string searchStr)
        {
            //Stopwatch sw = new Stopwatch();
            //sw.Start();
            TrieNode current = FindPrefix(searchStr);
            if (current != null)
            {
                Console.WriteLine("Found " + searchStr + " in:");
                DocumentLog visitedDocs = null;
                visitedDocs = PrintAllFromPrefix(current, visitedDocs);
                //sw.Stop();
                //Console.WriteLine($"Search took: {sw.Elapsed}");
                return true;
            }
            else
            {
                //sw.Stop();
                //Console.WriteLine($"Search took: {sw.Elapsed}");
                Console.WriteLine("No matches for " + searchStr + " found");
                return false;
            }
        }
    }
}