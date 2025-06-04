using System;
using System.Collections.Generic;
using SearchEngine.Core;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Tokenizers;

class ManualTest
{
    static void Main()
    {
        Console.WriteLine("Testing CompactTrieIndex functionality after refactoring...\n");
        
        // Create a CompactTrieIndex
        var index = new CompactTrieIndex();
        var tokenizer = new WhitespaceTokenizer();
        var analyzer = new StandardAnalyzer(tokenizer);
        
        // Add some test documents
        var documents = new List<(int, string)>
        {
            (1, "The quick brown fox jumps over the lazy dog"),
            (2, "A quick brown fox is very fast"),
            (3, "The dog is lazy and sleeps all day"),
            (4, "Programming is fun and rewarding"),
            (5, "Quick decisions make quick results")
        };
        
        Console.WriteLine("Adding documents to CompactTrieIndex...");
        foreach (var (docId, content) in documents)
        {
            var tokens = analyzer.Analyze(content);
            index.AddDocument(docId, tokens);
        }
        
        Console.WriteLine("Documents added successfully!\n");
        
        // Test exact search
        Console.WriteLine("=== Testing Exact Search ===");
        TestExactSearch(index, "quick");
        TestExactSearch(index, "fox");
        TestExactSearch(index, "lazy");
        TestExactSearch(index, "programming");
        TestExactSearch(index, "nonexistent");
        
        // Test prefix search
        Console.WriteLine("\n=== Testing Prefix Search ===");
        TestPrefixSearch(index, "qui");
        TestPrefixSearch(index, "pro");
        TestPrefixSearch(index, "la");
        TestPrefixSearch(index, "xyz");
        
        // Test memory stats
        Console.WriteLine("\n=== Testing Memory Stats ===");
        var stats = index.GetMemoryStats();
        Console.WriteLine($"Memory usage: {stats.memoryUsed} bytes");
        Console.WriteLine($"Bit index built: {stats.bitBuilt}");
        
        // Test max doc ID
        Console.WriteLine("\n=== Testing Max Doc ID ===");
        Console.WriteLine($"Max doc ID for 'quick': {index.GetMaxDocIdForKey("quick")}");
        Console.WriteLine($"Max doc ID for 'dog': {index.GetMaxDocIdForKey("dog")}");
        Console.WriteLine($"Max doc ID for 'nonexistent': {index.GetMaxDocIdForKey("nonexistent")}");
        
        Console.WriteLine("\n=== All tests completed successfully! ===");
    }
    
    static void TestExactSearch(CompactTrieIndex index, string term)
    {
        Console.WriteLine($"Exact search for '{term}':");
        var results = index.ExactSearch(term);
        if (results.Count > 0)
        {
            Console.WriteLine($"  Found {results.Count} documents:");
            foreach (var (docId, score) in results)
            {
                Console.WriteLine($"    Doc {docId}: score {score}");
            }
        }
        else
        {
            Console.WriteLine("  No documents found");
        }
    }
    
    static void TestPrefixSearch(CompactTrieIndex index, string prefix)
    {
        Console.WriteLine($"Prefix search for '{prefix}':");
        var results = index.PrefixSearch(prefix);
        if (results.Count > 0)
        {
            Console.WriteLine($"  Found {results.Count} documents:");
            foreach (var (docId, score) in results)
            {
                Console.WriteLine($"    Doc {docId}: score {score}");
            }
        }
        else
        {
            Console.WriteLine("  No documents found");
        }
    }
}
