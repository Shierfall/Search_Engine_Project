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
[IterationCount(7)]
public class IndexConstructionBenchmark
{
    private string[] _fileSizes = new[] { "100KB", "1MB", "2MB", "5MB", "10MB", "20MB", "50MB", "100MB", "200MB", "400MB" };
    private string _basePath = "/zhome/6b/1/188023/Downloads/texts/"; // adjust this path to your local environment
    private Analyzer _analyzer;
    private IExactPrefixIndex _trie;
    private IFullTextIndex _invertedIndex;
    private IBloomFilter _bloomFilter;
    private string _currentFile;

    [ParamsSource(nameof(FileSizes))]
    public string FileSize { get; set; }

    public IEnumerable<string> FileSizes => _fileSizes;

    [GlobalSetup]
    public void Setup()
    {
        _analyzer = new Analyzer(new MinimalTokenizer());
        _trie = new CompactTrieIndex();
        _invertedIndex = new SimpleInvertedIndex();
        _bloomFilter = new BloomFilter(2000000, 0.03); // assuming max 1M unique terms
        _currentFile = Path.Combine(_basePath, $"{FileSize}.txt");
    }

    [Benchmark]
    public void TrieConstruction()
    {
        ProcessMultipleDocuments(doc => {
            var tokens = _analyzer.Analyze(doc.content).ToList();
            _trie.AddDocument(doc.id, tokens);
        });
    }

    [Benchmark]
    public void InvertedIndexConstruction()
    {
        ProcessMultipleDocuments(doc => {
            var tokens = _analyzer.Analyze(doc.content).ToList();
            _invertedIndex.AddDocument(doc.id, tokens);
        });
    }

    [Benchmark]
    public void BloomFilterConstruction()
    {
        ProcessMultipleDocuments(doc => {
            var tokens = _analyzer.Analyze(doc.content).ToList();
            foreach (var token in tokens)
            {
                _bloomFilter.Add(token.Term);
            }
        });
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
                    string content = sb.ToString().Trim();
                    processDocument((docId++, content));
                }
                
         
                currentTitle = null;
                sb.Clear();
            }
            else
            {
                         sb.AppendLine(line);
            }
        }
        if (currentTitle != null && sb.Length > 0)
        {
            string content = sb.ToString().Trim();
            processDocument((docId, content));
        }
    }

    public void PrintMemoryUsage(bool exportToCsv = false)
    {
        Console.WriteLine("\nMemory Usage Analysis");
        Console.WriteLine("====================");
        
        // create a list to store results for CSV export
        var results = new List<(string FileSize, int TotalTokens, int UniqueTokens, 
            long TrieMemory, long InvertedIndexMemory, long InvertedIndexNoPositionsMemory, 
            long BloomFilterMemory, long TotalMemory)>();

        foreach (var fileSize in _fileSizes)
        {
            var filePath = Path.Combine(_basePath, $"{fileSize}.txt");
            try
            {
                Console.WriteLine($"\nProcessing {fileSize} file: {filePath}");
                string fileContent = File.ReadAllText(filePath);
                
                Console.WriteLine($"Memory Usage for {fileSize} file:");
                Console.WriteLine("----------------------------------------");

                // reset data structures for each file size
                var analyzer = new Analyzer(new MinimalTokenizer());
                var trie = new CompactTrieIndex();
                var invertedIndex = new SimpleInvertedIndex();
                var bloomFilter = new BloomFilter(1000000*20, 0.01);

                // analyze tokens
                var tokens = analyzer.Analyze(fileContent).ToList();
                int totalTokens = tokens.Count;
                int uniqueTokens = tokens.Select(t => t.Term).Distinct().Count();
                Console.WriteLine($"Total Tokens: {totalTokens}");
                Console.WriteLine($"Unique Tokens: {uniqueTokens}");

                // calculate Trie memory
                trie.AddDocument(1, tokens);
                long trieMemory = CalculateTrieMemory((CompactTrieIndex)trie);
                Console.WriteLine($"Trie Index: {FormatBytes(trieMemory)}");

                // calculate Inverted Index memory with positions
                invertedIndex.AddDocument(1, tokens);
                long invertedIndexMemory = CalculateInvertedIndexMemory((SimpleInvertedIndex)invertedIndex, true);
                Console.WriteLine($"Inverted Index: {FormatBytes(invertedIndexMemory)}");
                
                // calculate Inverted Index memory without positions
                long invertedIndexNoPositionsMemory = CalculateInvertedIndexMemory((SimpleInvertedIndex)invertedIndex, false);
                Console.WriteLine($"Inverted Index (no positions): {FormatBytes(invertedIndexNoPositionsMemory)}");

                // calculate Bloom Filter memory
                foreach (var token in tokens)
                {
                    bloomFilter.Add(token.Term);
                }
                long bloomFilterMemory = CalculateBloomFilterMemory((BloomFilter)bloomFilter);
                Console.WriteLine($"Bloom Filter: {FormatBytes(bloomFilterMemory)}");

                // calculate total memory
                long totalMemory = trieMemory + invertedIndexMemory + bloomFilterMemory;
                Console.WriteLine($"Total Memory: {FormatBytes(totalMemory)}");
                Console.WriteLine("----------------------------------------");

                // add results to our list for CSV export
                results.Add((fileSize, totalTokens, uniqueTokens, trieMemory, 
                    invertedIndexMemory, invertedIndexNoPositionsMemory,
                    bloomFilterMemory, totalMemory));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file {filePath}: {ex.Message}");
                continue;
            }
        }
        
        if (exportToCsv)
        {
            ExportResultsToCsv(results);
        }
    }
    
private void ExportResultsToCsv(List<(string FileSize, int TotalTokens, int UniqueTokens, 
    long TrieMemory, long InvertedIndexMemory, long InvertedIndexNoPositionsMemory, 
    long BloomFilterMemory, long TotalMemory)> results)
{
    string csvFileName = $"memory_usage_analysis_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
    using (var writer = new StreamWriter(csvFileName))
    {
        // Updated header with no positions column
        writer.WriteLine("File Size,Total Tokens,Unique Tokens," +
            "Trie Memory (Bytes),Trie Memory (MB)," +
            "Inverted Index Memory (Bytes),Inverted Index Memory (MB)," +
            "Inverted Index No Positions (Bytes),Inverted Index No Positions (MB)," +
            "Bloom Filter Memory (Bytes),Bloom Filter Memory (MB)," +
            "Total Memory (Bytes),Total Memory (MB)");
        
        // Updated data rows
        foreach (var result in results)
        {
            writer.WriteLine(
                $"{result.FileSize}," +
                $"{result.TotalTokens},{result.UniqueTokens}," +
                $"{result.TrieMemory},{FormatBytes(result.TrieMemory)}," +
                $"{result.InvertedIndexMemory},{FormatBytes(result.InvertedIndexMemory)}," +
                $"{result.InvertedIndexNoPositionsMemory},{FormatBytes(result.InvertedIndexNoPositionsMemory)}," +
                $"{result.BloomFilterMemory},{FormatBytes(result.BloomFilterMemory)}," +
                $"{result.TotalMemory},{FormatBytes(result.TotalMemory)}"
            );
        }
    }
        
        Console.WriteLine($"\nResults exported to {Path.GetFullPath(csvFileName)}");
    }

    private long CalculateTrieMemory(CompactTrieIndex trie)
    {
        // constants for object overhead
        const int OBJECT_OVERHEAD = 16; // object header (12) + padding (4)
        const int LIST_OVERHEAD = 32;   // object header + internal array reference + size fields
        const int DICT_OVERHEAD = 48;   // dictionary object overhead

        // memory for TrieNode structure
        // base fields: PoolIndex, Offset, Length (3 ints), IsEndOfWord (bool)
        // children references: ArrayChildren (26 refs), DictChildren reference
        int trieNodeSize = OBJECT_OVERHEAD + 
                          (sizeof(int) * 3) + 
                          sizeof(bool) + 
                          OBJECT_OVERHEAD + (IntPtr.Size * 26) +  // array overhead + 26 pointers
                          DICT_OVERHEAD;                          // dictionary overhead

        // add average memory for DocIds List per node
        // assume average 5 document IDs per node that is end of word (guesstimate)
        int avgDocsPerEndNode = 5;
        // estimating 20% end nodes
        double endNodeRatio = 0.2;
        int endNodeCount = (int)(trie.TotalNodeCount * endNodeRatio);
        long docIdsMemory = endNodeCount * (LIST_OVERHEAD + (sizeof(int) * avgDocsPerEndNode));

        // memory for all TrieNodes
        long totalTrieNodeMemory = trie.TotalNodeCount * trieNodeSize + docIdsMemory;

        // memory for word pool (List<string>)
        long wordPoolMemory = LIST_OVERHEAD;  // the List object itself
        foreach (var word in trie.WordPool)
        {
            // string overhead + content
            wordPoolMemory += OBJECT_OVERHEAD + (word.Length * sizeof(char));
        }

        // memory for word-to-pool index mapping (Dictionary<string, int>)
        long wordToPoolIndexMemory = DICT_OVERHEAD;  // dictionary overhead
        foreach (var kvp in trie.WordToPoolIndex)
        {
            // string key overhead + content + int value
            wordToPoolIndexMemory += OBJECT_OVERHEAD + (kvp.Key.Length * sizeof(char)) + sizeof(int);
        }

        return totalTrieNodeMemory + wordPoolMemory + wordToPoolIndexMemory;
    }

    private long CalculateInvertedIndexMemory(SimpleInvertedIndex invertedIndex, bool includePositions = true)
    {
        // constants for object overhead
        const int OBJECT_OVERHEAD = 16; // object header (12) + padding (4)
        const int LIST_OVERHEAD = 32;   // object header + internal array reference + size fields
        const int DICT_OVERHEAD = 48;   // dictionary overhead

        // memory for the main Dictionary object (_map)
        long mapMemory = DICT_OVERHEAD;
        
        // memory for postings and strings
        long postingsMemory = 0;
        
        foreach (var kvp in invertedIndex.Map)
        {
            // string key overhead + content
            mapMemory += OBJECT_OVERHEAD + (kvp.Key.Length * sizeof(char));
            
            // List<Posting> overhead
            postingsMemory += LIST_OVERHEAD;
            
            // each posting object
            foreach (var posting in kvp.Value)
            {
                // posting object overhead + DocId + Count fields
                long postingSize = OBJECT_OVERHEAD + sizeof(int) * 2;
                if (includePositions)
                {
                    postingSize += LIST_OVERHEAD + (posting.Positions.Count * sizeof(int));
                }
                // List<int> Positions overhead + content

                    postingsMemory += postingSize;
            }
        }
        
        // add memory for _bitIndex dictionary (not calculated in current implementation)
        // we know the bitIndex uses the same keys as the map, but has BitArray values
        long bitIndexMemory = DICT_OVERHEAD;
        
        foreach (var key in invertedIndex.Map.Keys)
        {
            // string key overhead + content (key is shared with _map but reference is duplicated)
            bitIndexMemory += IntPtr.Size;  // just reference to existing string
            
            // bitArray is approximately size/8 bytes plus overhead
            // average size would be based on the number of documents
            // Let's estimate the document count as the highest docId in any posting
            int maxDocId = 0;
            foreach (var posting in invertedIndex.Map[key])
            {
                maxDocId = Math.Max(maxDocId, posting.DocId);
            }
            
            // bitArray overhead + storage
            bitIndexMemory += OBJECT_OVERHEAD + ((maxDocId + 7) / 8);
        }

        return mapMemory + postingsMemory + bitIndexMemory;
    }

    private long CalculateBloomFilterMemory(BloomFilter bloomFilter)
    {
        // constants for object overhead
        const int OBJECT_OVERHEAD = 16; // object header (12) + padding (4)
        
        // memory for BloomFilter object itself
        long bloomFilterObjectMemory = OBJECT_OVERHEAD + sizeof(int); // _hashCount field
        
        // memory for BitArray
        // the BitArray has an object overhead, plus an internal int array for storage
        // each int stores 32 bits, so we need (Length + 31) / 32 ints
        int intsRequired = (bloomFilter.BitArray.Length + 31) / 32;
        long bitArrayMemory = OBJECT_OVERHEAD +            // bitArray object overhead
                             sizeof(int) +                 // Length field
                             OBJECT_OVERHEAD +            // int[] array overhead
                             (intsRequired * sizeof(int)); // actual storage
        
        return bloomFilterObjectMemory + bitArrayMemory;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number = number / 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }
}