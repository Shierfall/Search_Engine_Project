using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SearchEngine.Analysis;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Analysis.Tokenizers;
using System.Text;
using System.Runtime.InteropServices;

namespace SearchEngine.Benchmarks;

[MemoryDiagnoser]
[WarmupCount(1)]
[IterationCount(4)]
public class IndexConstructionBenchmark
{
    private string[] _fileSizes = new[] { "100KB", "1MB", "2MB", "5MB", "10MB", "20MB", "50MB", "100MB", "200MB", "400MB"};
    private string _basePath = "/zhome/6b/1/188023/Downloads/texts/"; // adjust this path to your local environment
    private Analyzer _analyzer;
    private IExactPrefixIndex _trie;
    private IFullTextIndex _invertedIndex;
    private IBloomFilter _bloomFilter;
    private string _currentFile;
    private int _documentsIndexed;
    private int _tokensProcessed;
    private string _currentContent;

    [ParamsSource(nameof(FileSizes))]
    public string FileSize { get; set; }

    public IEnumerable<string> FileSizes => _fileSizes;

    [GlobalSetup]
    public void Setup()
    {
        _analyzer = new Analyzer(new MinimalTokenizer());
        _trie = new CompactTrieIndex();
        _trie.SetUseBM25(false);
        _invertedIndex = new InvertedIndex(); // Use the new optimized InvertedIndex
        _bloomFilter = new BloomFilter(8000000, 0.01); // assuming max 8M unique terms
        _currentFile = Path.Combine(_basePath, $"{FileSize}.txt");
        if (Environment.GetEnvironmentVariable("BENCHMARK_VERBOSE") == "1")
        {
            Console.WriteLine($"Loading file: {_currentFile}");
        }
        _documentsIndexed = 0;
        _tokensProcessed = 0;
        
        try
        {
            using var reader = new StreamReader(_currentFile, Encoding.UTF8);
            StringBuilder sb = new StringBuilder();
            string? line;
            bool foundFirstDoc = false;
            string? currentTitle = null;
            
            while ((line = reader.ReadLine()) != null)
            {
                if (currentTitle == null)
                {
                    // first non-empty line is the title
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        currentTitle = line;
                        foundFirstDoc = true;
                    }
                }
                else if (line.Trim() == "---END.OF.DOCUMENT---")
                {
                    // end of first document
                    break;
                }
                else if (foundFirstDoc)
                {
                    sb.AppendLine(line);
                }
            }
            
            _currentContent = sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading content: {ex.Message}");
            _currentContent = "Sample text for benchmark";
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _trie = null;
        _invertedIndex = null;
        _bloomFilter = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Benchmark]
    public void TrieConstruction()
    {
        int docCount = 0;
        int tokenCount = 0;
        ProcessMultipleDocuments(doc => {
            var tokens = _analyzer.Analyze(doc.content).ToList();
            _trie.AddDocument(doc.id, tokens);
            docCount++;
            tokenCount += tokens.Count;
        });
        _documentsIndexed = docCount;
        _tokensProcessed = tokenCount;
        if (Environment.GetEnvironmentVariable("BENCHMARK_VERBOSE") == "1")
        {
            Console.WriteLine($"Trie: Documents indexed: {docCount}, Tokens: {tokenCount}");
        }
    }

    [Benchmark]
    public void InvertedIndexConstruction()
    {
        int docCount = 0;
        int tokenCount = 0;
        ProcessMultipleDocuments(doc => {
            var tokens = _analyzer.Analyze(doc.content).ToList();
            _invertedIndex.AddDocument(doc.id, tokens);
            docCount++;
            tokenCount += tokens.Count;
        });
        _documentsIndexed = docCount;
        _tokensProcessed = tokenCount;
        if (Environment.GetEnvironmentVariable("BENCHMARK_VERBOSE") == "1")
        {
            Console.WriteLine($"InvertedIndex: Documents indexed: {docCount}, Tokens: {tokenCount}");
        }
    }

    [Benchmark]
    public void BloomFilterConstruction()
    {
        int docCount = 0;
        int tokenCount = 0;
        ProcessMultipleDocuments(doc => {
            var tokens = _analyzer.Analyze(doc.content).ToList();
            foreach (var token in tokens)
            {
                _bloomFilter.Add(token.Term);
            }
            docCount++;
            tokenCount += tokens.Count;
        });
        _documentsIndexed = docCount;
        _tokensProcessed = tokenCount;
        if (Environment.GetEnvironmentVariable("BENCHMARK_VERBOSE") == "1")
        {
            Console.WriteLine($"BloomFilter: Documents indexed: {docCount}, Tokens: {tokenCount}");
        }
    }
    
    private void ProcessMultipleDocuments(Action<(int id, string content)> processDocument)
    {
        using var reader = new StreamReader(_currentFile, Encoding.UTF8);
        string? line;
        string? currentTitle = null;
        var sb = new StringBuilder();
        int docId = 1;
        
        while ((line = reader.ReadLine()) != null)
        {
            if (currentTitle == null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    currentTitle = line;
                }
            }
            else if (line.Trim() == "---END.OF.DOCUMENT---")
            {
                if (sb.Length > 0)
                {
                    processDocument((docId++, sb.ToString().Trim()));
                    sb.Clear();
                }

                currentTitle = null;
            }
            else
            {
                // regular content line
                sb.AppendLine(line);
            }
        }
        
        // process the last document if there was no final marker
        if (currentTitle != null && sb.Length > 0)
        {
            processDocument((docId, sb.ToString().Trim()));
        }
    }

    public void PrintMemoryUsage()
    {
        Console.WriteLine($"\nMemory Usage for {FileSize} file:");
        Console.WriteLine("----------------------------------------");

        // Create indexes to measure memory
        var compactTrie = new CompactTrieIndex();
        compactTrie.SetUseBM25(false); // disable BM25 for fair comparison
        var invertedIndex = new InvertedIndex();
        var bloomFilter = new BloomFilter(1000000, 0.01);

        // Process documents the same way as benchmarks to get realistic memory usage
        int totalTokens = 0;
        int documentCount = 0;
        var allUniqueTokens = new HashSet<string>();

        ProcessMultipleDocuments(doc => {
            var tokens = _analyzer.Analyze(doc.content).ToList();
            
            // Add to indexes with proper document IDs (not just 1!)
            compactTrie.AddDocument(doc.id, tokens);
            invertedIndex.AddDocument(doc.id, tokens);
            
            // Add to bloom filter
            foreach (var token in tokens)
            {
                bloomFilter.Add(token.Term);
                allUniqueTokens.Add(token.Term);
            }
            
            totalTokens += tokens.Count;
            documentCount++;
        });

        // Calculate memory usage
        long trieMemory = CalculateActualTrieMemory(compactTrie, false);
        Console.WriteLine($"Trie Index (no BM25): {FormatBytes(trieMemory)}");

        long invertedIndexMemory = CalculateActualInvertedIndexMemory(invertedIndex, false);
        Console.WriteLine($"Unified Inverted Index: {FormatBytes(invertedIndexMemory)}");

        long bloomFilterMemory = CalculateBloomFilterMemory(bloomFilter);
        Console.WriteLine($"Bloom Filter: {FormatBytes(bloomFilterMemory)}");

        // calculate total memory usage
        long totalMemory = trieMemory + invertedIndexMemory + bloomFilterMemory;
        Console.WriteLine($"Total Memory: {FormatBytes(totalMemory)}");
        Console.WriteLine($"Total Tokens: {totalTokens}");
        Console.WriteLine($"Unique Tokens: {allUniqueTokens.Count}");
        Console.WriteLine($"Documents Processed: {documentCount}");

        ExportMemoryUsageToCsv(FileSize, totalTokens, allUniqueTokens.Count, trieMemory, invertedIndexMemory, bloomFilterMemory);

        Console.WriteLine("----------------------------------------\n");
    }

    private long CalculateActualTrieMemory(CompactTrieIndex trie, bool includePositions)
    {
        const int OBJECT_OVERHEAD = 16;
        const int LIST_OVERHEAD = 32;
        const int DICT_OVERHEAD = 48;

        // Get actual built data from the trie
        var allNodes = trie.GetAllNodes().ToList();
        int actualNodeCount = allNodes.Count;
        int actualDocumentCount = trie.DocumentCount;
        int actualWordPoolSize = trie.WordPool.Count;
        int actualWordToPoolIndexSize = trie.WordToPoolIndex.Count;
        
        Console.WriteLine($"  Actual Trie Stats - Nodes: {actualNodeCount}, WordPool: {actualWordPoolSize}, WordToPoolIndex: {actualWordToPoolIndexSize}, Docs: {actualDocumentCount}");

        // Calculate memory for actual TrieNode objects
        long trieNodesMemory = 0;
        int maxDocId = 0;
        int totalDocIdsCount = 0;
        int totalPositionsCount = 0;
        
        foreach (var node in allNodes)
        {
            // TrieNode object overhead + fields:
            // - PoolIndex, Offset, Length (3 ints)
            // - IsEndOfWord (bool)
            // - ArrayChildren reference (IntPtr)
            // - DictChildren reference (IntPtr) 
            // - DocIds reference (IntPtr)
            // - Positions reference (IntPtr)
            long nodeMemory = OBJECT_OVERHEAD + 
                             (sizeof(int) * 3) +     // PoolIndex, Offset, Length
                             sizeof(bool) +          // IsEndOfWord
                             (IntPtr.Size * 4);      // References to ArrayChildren, DictChildren, DocIds, Positions

            // ArrayChildren array (always 26 elements)
            nodeMemory += OBJECT_OVERHEAD + (IntPtr.Size * 26);

            // DictChildren dictionary
            nodeMemory += DICT_OVERHEAD;
            if (node.DictChildren != null && node.DictChildren.Count > 0)
            {
                nodeMemory += node.DictChildren.Count * (sizeof(char) + IntPtr.Size);
            }

            // DocIds list
            if (node.DocIds != null)
            {
                nodeMemory += LIST_OVERHEAD + (node.DocIds.Count * sizeof(int));
                totalDocIdsCount += node.DocIds.Count;
                
                if (node.DocIds.Count > 0)
                {
                    int localMaxDocId = node.DocIds.Max();
                    maxDocId = Math.Max(maxDocId, localMaxDocId);
                }
            }

            // Positions dictionary
            if (node.Positions != null)
            {
                nodeMemory += DICT_OVERHEAD;
                foreach (var positionsList in node.Positions.Values)
                {
                    if (positionsList != null)
                    {
                        nodeMemory += LIST_OVERHEAD + (positionsList.Count * sizeof(int));
                        totalPositionsCount += positionsList.Count;
                    }
                }
                // Dictionary entries overhead (docId -> List<int>)
                nodeMemory += node.Positions.Count * (sizeof(int) + IntPtr.Size);
            }

            trieNodesMemory += nodeMemory;
        }

        // Calculate actual word pool memory (List<string>)
        long wordPoolMemory = LIST_OVERHEAD;
        foreach (var word in trie.WordPool)
        {
            wordPoolMemory += OBJECT_OVERHEAD + (word.Length * sizeof(char));
        }

        // Calculate actual word to pool index memory (Dictionary<string, int>)
        long wordToPoolIndexMemory = DICT_OVERHEAD;
        foreach (var kvp in trie.WordToPoolIndex)
        {
            // String key is shared from word pool, so we only count the reference + int value
            wordToPoolIndexMemory += IntPtr.Size + sizeof(int);
        }

        // Calculate bit index memory (_bitIndex: Dictionary<string, BitArray>)
        long bitIndexMemory = DICT_OVERHEAD;
        if (maxDocId > 0)
        {
            int bitArraySize = OBJECT_OVERHEAD + ((maxDocId + 7) / 8);
            // Each word in the index could have a BitArray
            bitIndexMemory += actualWordToPoolIndexSize * (IntPtr.Size + bitArraySize);
        }

        // Calculate doc lengths memory (_docLengths: Dictionary<int, int>)
        long docLengthsMemory = DICT_OVERHEAD + (actualDocumentCount * (sizeof(int) + sizeof(int)));

        // Other CompactTrieIndex fields
        long otherFieldsMemory = sizeof(double) * 3 +  // _avgDocLength, _k1, _b
                                sizeof(int) * 2 +      // _totalDocs, _nextDocId 
                                sizeof(bool) * 3 +     // _delta, _useBM25, _bitBuilt
                                OBJECT_OVERHEAD * 3;   // lock objects

        long totalMemory = trieNodesMemory + wordPoolMemory + wordToPoolIndexMemory + bitIndexMemory + docLengthsMemory + otherFieldsMemory;

        Console.WriteLine($"  Trie Memory Breakdown - Nodes: {FormatBytes(trieNodesMemory)}, WordPool: {FormatBytes(wordPoolMemory)}, WordIndex: {FormatBytes(wordToPoolIndexMemory)}, BitIndex: {FormatBytes(bitIndexMemory)}, DocLengths: {FormatBytes(docLengthsMemory)}");
        Console.WriteLine($"  Total DocIds: {totalDocIdsCount}, Total Positions: {totalPositionsCount}, MaxDocId: {maxDocId}");

        return totalMemory;
    }

    private long CalculateActualInvertedIndexMemory(InvertedIndex invertedIndex, bool includePositions)
    {
        const int OBJECT_OVERHEAD = 16;
        const int LIST_OVERHEAD = 32;
        const int DICT_OVERHEAD = 48;
        const int HASHSET_OVERHEAD = 48;
        const int BITARRAY_OVERHEAD = 24;

        // Get actual statistics from the built index
        var stats = invertedIndex.GetMemoryStats();
        int actualTermCount = stats.termCount;
        int actualTotalPostings = stats.totalPostings;
        int actualTotalPositions = stats.totalPositions;
        int actualMaxDocId = stats.maxDocId;
        bool actualBitsBuilt = stats.bitsBuilt;
        int actualDocumentCount = invertedIndex.GetDocumentCount();

        Console.WriteLine($"  Actual InvertedIndex Stats - Terms: {actualTermCount}, Postings: {actualTotalPostings}, Positions: {actualTotalPositions}, MaxDocId: {actualMaxDocId}, Docs: {actualDocumentCount}");

        // Main _termIndex dictionary overhead
        long totalMemory = DICT_OVERHEAD;
        
        // Calculate memory for actual TermEntry structures
        foreach (int termIndex in Enumerable.Range(0, actualTermCount))
        {
            // TermEntry object overhead
            totalMemory += OBJECT_OVERHEAD;
            
            // Postings dictionary (Dictionary<int, Posting>) - estimate average postings per term
            totalMemory += DICT_OVERHEAD;
            int avgPostingsPerTerm = Math.Max(1, actualTotalPostings / actualTermCount);
            
            // Memory for actual Posting objects
            for (int j = 0; j < avgPostingsPerTerm; j++)
            {
                // Posting object: DocId (int) + Count (int) + Positions (List<int>)
                totalMemory += OBJECT_OVERHEAD + sizeof(int) * 2; // DocId + Count
                
                if (includePositions && actualTotalPositions > 0)
                {
                    // Positions list - use actual average positions per posting
                    int avgPositionsPerPosting = Math.Max(1, actualTotalPositions / actualTotalPostings);
                    totalMemory += LIST_OVERHEAD + (sizeof(int) * avgPositionsPerPosting);
                }
            }
            
            // DocSet (HashSet<int>) for O(1) document lookups
            totalMemory += HASHSET_OVERHEAD + (sizeof(int) * avgPostingsPerTerm);
            
            // BitIndex (BitArray?) - only if bits are actually built
            if (actualBitsBuilt && actualMaxDocId >= 0)
            {
                totalMemory += BITARRAY_OVERHEAD + ((actualMaxDocId + 7) / 8);
            }
        }
        
        // Document lengths tracking (_docLengths dictionary) - use actual document count
        totalMemory += DICT_OVERHEAD + (sizeof(int) * actualDocumentCount);
        
        // BM25 statistics fields
        totalMemory += sizeof(double) * 3 + sizeof(int) * 2; // _avgDocLength, _k1, _b, _totalDocs, _nextDocId
        
        // Thread synchronization objects  
        totalMemory += OBJECT_OVERHEAD * 2; // _termLock, _statsLock
        
        return totalMemory;
    }

    private long CalculateBloomFilterMemory(BloomFilter bloomFilter)
    {
        const int OBJECT_OVERHEAD = 16;
        long bloomFilterObjectMemory = OBJECT_OVERHEAD + sizeof(int);
        int intsRequired = (bloomFilter.BitArray.Length + 31) / 32;
        long bitArrayMemory = OBJECT_OVERHEAD +
                             sizeof(int) +
                             OBJECT_OVERHEAD +
                             (intsRequired * sizeof(int));
        return bloomFilterObjectMemory + bitArrayMemory;
    }

    private void ExportMemoryUsageToCsv(string fileSize, int totalTokens, int uniqueTokens, 
        long trieMemory, long invertedIndexMemory, long bloomFilterMemory)
    {
        var csvPath = Path.Combine(Directory.GetCurrentDirectory(), "MemoryUsageResults.csv");
        bool fileExists = File.Exists(csvPath);
        
        using var writer = new StreamWriter(csvPath, append: fileExists);
        
        if (!fileExists)
        {
            writer.WriteLine("File Size,Total Tokens,Unique Tokens," +
                             "Trie Memory (Bytes),Trie Memory (MB)," + 
                             "Unified Inverted Index Memory (Bytes),Unified Inverted Index Memory (MB)," + 
                             "Bloom Filter Memory (Bytes),Bloom Filter Memory (MB)," + 
                             "Total Memory (Bytes),Total Memory (MB)");
        }
        
        // Calculate total memory
        long totalMemory = trieMemory + invertedIndexMemory + bloomFilterMemory;
        
        // Calculate MB values
        double trieMemoryMB = trieMemory / (1024.0 * 1024.0);
        double invertedIndexMemoryMB = invertedIndexMemory / (1024.0 * 1024.0);
        double bloomFilterMemoryMB = bloomFilterMemory / (1024.0 * 1024.0);
        double totalMemoryMB = totalMemory / (1024.0 * 1024.0);
        
        writer.WriteLine($"{fileSize},{totalTokens},{uniqueTokens}," + 
                         $"{trieMemory},{trieMemoryMB:F2}," + 
                         $"{invertedIndexMemory},{invertedIndexMemoryMB:F2}," + 
                         $"{bloomFilterMemory},{bloomFilterMemoryMB:F2}," + 
                         $"{totalMemory},{totalMemoryMB:F2}");
        
        Console.WriteLine($"Memory usage results exported to {csvPath}");
    }

    private string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        if (bytes >= GB)
            return $"{bytes / (double)GB:F2} GB";
        if (bytes >= MB)
            return $"{bytes / (double)MB:F2} MB";
        if (bytes >= KB)
            return $"{bytes / (double)KB:F2} KB";

        return $"{bytes} Bytes";
    }
}