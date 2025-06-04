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
    private string _basePath = "/home/shierfall/Downloads/texts/"; // adjust this path to your local environment
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

        // load content from the actual file to ensure we're measuring based on file size
        string content;
        try
        {
            using var reader = new StreamReader(_currentFile, Encoding.UTF8);
            content = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading content for memory analysis: {ex.Message}");
            content = _currentContent; // fallback to cached content
        }

        // measure memory for CompactTrieIndex
        var trieTokens = _analyzer.Analyze(content).ToList();
        int totalTokens = trieTokens.Count;
        // count unique tokens
        var uniqueTokens = new HashSet<string>(trieTokens.Select(t => t.Term)).Count;
        
        var compactTrie = new CompactTrieIndex();
        compactTrie.SetUseBM25(false); // disable BM25 for fair comparison
        compactTrie.AddDocument(1, trieTokens);
        long trieMemory = CalculateTrieMemory(compactTrie, false);
        Console.WriteLine($"Trie Index (no BM25): {FormatBytes(trieMemory)}");

        // measure memory for optimized InvertedIndex
        var invertedIndex = new InvertedIndex();
        invertedIndex.AddDocument(1, trieTokens);
        long invertedIndexMemory = CalculateUnifiedInvertedIndexMemory(invertedIndex, false);
        Console.WriteLine($"Unified Inverted Index: {FormatBytes(invertedIndexMemory)}");

        // measure memory for BloomFilter
        var bloomFilter = new BloomFilter(1000000, 0.01);
        foreach (var token in trieTokens)
        {
            bloomFilter.Add(token.Term);
        }
        long bloomFilterMemory = CalculateBloomFilterMemory(bloomFilter);
        Console.WriteLine($"Bloom Filter: {FormatBytes(bloomFilterMemory)}");

        // calculate total memory usage
        long totalMemory = trieMemory + invertedIndexMemory + bloomFilterMemory;
        Console.WriteLine($"Total Memory: {FormatBytes(totalMemory)}");
        Console.WriteLine($"Total Tokens: {totalTokens}");
        Console.WriteLine($"Unique Tokens: {uniqueTokens}");

        ExportMemoryUsageToCsv(FileSize, totalTokens, uniqueTokens, trieMemory, invertedIndexMemory, bloomFilterMemory);

        Console.WriteLine("----------------------------------------\n");
    }

    private long CalculateTrieMemory(CompactTrieIndex trie, bool includePositions)
    {
        const int OBJECT_OVERHEAD = 16;
        const int LIST_OVERHEAD = 32;
        const int DICT_OVERHEAD = 48;

        int trieNodeSize = OBJECT_OVERHEAD + 
                          (sizeof(int) * 3) + 
                          sizeof(bool) + 
                          OBJECT_OVERHEAD + (IntPtr.Size * 26) +  
                          DICT_OVERHEAD;

        // calculate all memory components in a single pass through the nodes
        var allNodes = trie.GetAllNodes().ToList();
        int nodeCount = allNodes.Count;
        long docIdsMemory = 0;
        int maxDocId = 0;
        
        foreach (var node in allNodes)
        {
            // calculate memory for document IDs
            if (node.IsEndOfWord && node.DocIds != null && node.DocIds.Count > 0)
            {
                docIdsMemory += LIST_OVERHEAD + (node.DocIds.Count * sizeof(int));
                
                // Find max docId for bit array size calculation
                int localMaxDocId = node.DocIds.Max();
                maxDocId = Math.Max(maxDocId, localMaxDocId);
            }
            
            // add position data if required
            if (includePositions && node.Positions != null)
            {
                foreach (var positions in node.Positions.Values)
                {
                    if (positions != null)
                    {
                        docIdsMemory += LIST_OVERHEAD + (positions.Count * sizeof(int));
                    }
                }
            }
        }

        long totalTrieNodeMemory = nodeCount * trieNodeSize + docIdsMemory;

        // calculate word pool memory
        long wordPoolMemory = LIST_OVERHEAD;
        foreach (var word in trie.WordPool)
        {
            wordPoolMemory += OBJECT_OVERHEAD + (word.Length * sizeof(char));
        }

        // calculate word to pool index memory
        long wordToPoolIndexMemory = DICT_OVERHEAD;
        foreach (var kvp in trie.WordToPoolIndex)
        {
            wordToPoolIndexMemory += OBJECT_OVERHEAD + (kvp.Key.Length * sizeof(char)) + sizeof(int);
        }

        // calculate bit index memory
        long bitIndexMemory = DICT_OVERHEAD;
        int bitArraySize = maxDocId > 0 ? OBJECT_OVERHEAD + ((maxDocId + 7) / 8) : 0;
        
        // add memory for each key's bit array
        foreach (var _ in trie.WordToPoolIndex.Keys)
        {
            bitIndexMemory += IntPtr.Size; // reference to existing string
            if (bitArraySize > 0)
            {
                bitIndexMemory += bitArraySize;
            }
        }

        // calculate doc lengths memory
        long docLengthsMemory = trie.DocumentCount * sizeof(int);

        return totalTrieNodeMemory + wordPoolMemory + wordToPoolIndexMemory + bitIndexMemory + docLengthsMemory;
    }

    private long CalculateUnifiedInvertedIndexMemory(InvertedIndex invertedIndex, bool includePositions)
    {
        const int OBJECT_OVERHEAD = 16;
        const int LIST_OVERHEAD = 32;
        const int DICT_OVERHEAD = 48;
        const int HASHSET_OVERHEAD = 48;
        const int BITARRAY_OVERHEAD = 24;

        // Main _termIndex dictionary overhead
        long totalMemory = DICT_OVERHEAD;
        
        // Estimate memory based on typical document structure
        // Since we can't access private fields directly, we estimate based on document size
        int estimatedUniqueTerms = Math.Max(1, content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant()).Distinct().Count());
        int estimatedTotalTokens = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int estimatedPostings = Math.Min(estimatedTotalTokens, estimatedUniqueTerms * 2); // conservative estimate
        
        // Calculate memory for unified TermEntry structures
        for (int i = 0; i < estimatedUniqueTerms; i++)
        {
            // TermEntry object overhead
            totalMemory += OBJECT_OVERHEAD;
            
            // Postings dictionary (Dictionary<int, Posting>)
            totalMemory += DICT_OVERHEAD;
            int avgPostingsPerTerm = Math.Max(1, estimatedPostings / estimatedUniqueTerms);
            
            for (int j = 0; j < avgPostingsPerTerm; j++)
            {
                // Posting object: DocId (int) + Count (int) + Positions (List<int>)
                totalMemory += OBJECT_OVERHEAD + sizeof(int) * 2; // DocId + Count
                
                if (includePositions)
                {
                    // Positions list (estimated 3-5 positions per posting)
                    int avgPositionsPerPosting = 4;
                    totalMemory += LIST_OVERHEAD + (sizeof(int) * avgPositionsPerPosting);
                }
            }
            
            // DocSet (HashSet<int>) for O(1) document lookups - key optimization!
            totalMemory += HASHSET_OVERHEAD + (sizeof(int) * avgPostingsPerTerm);
            
            // BitIndex (BitArray?) - allocated when BuildBits() is called
            // Assume 1 document for this calculation
            totalMemory += BITARRAY_OVERHEAD + ((1 + 7) / 8); // minimal bit array for 1 doc
        }
        
        // Document lengths tracking (_docLengths dictionary)
        totalMemory += DICT_OVERHEAD + sizeof(int) * 2; // one document entry
        
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