using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Filters;
using SearchEngine.Analysis.Interfaces;
using SearchEngine.Analysis.Tokenizers;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using Porter2StemmerStandard;
using System.Text;

namespace SearchEngine.Benchmarks;

[MemoryDiagnoser]
[WarmupCount(1)]
[IterationCount(5)]
public class FilterAnalysisBenchmark
{
    private string[] _fileSizes = new[] { "100KB", "1MB", "2MB", "5MB", "10MB", "20MB", "50MB" }; // Limit to reasonable sizes for filter analysis
    private string _basePath = "/zhome/6b/1/188023/Downloads/texts/";
    
    // common English stop words
    private static readonly string[] StopWords = new[] {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", 
        "has", "he", "in", "is", "it", "its", "of", "on", "that", "the", 
        "to", "was", "were", "will", "with"
    };
    
    // Sample synonyms (can be expanded)
    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        {"quick", new[] {"fast", "rapid", "swift"}},
        {"slow", new[] {"sluggish", "unhurried"}},
        {"happy", new[] {"glad", "joyful", "pleased"}},
        {"sad", new[] {"unhappy", "sorrowful", "downcast"}}
    };

    public void RunFilterAnalysis()
    {
        Console.WriteLine("\nFilter Analysis");
        Console.WriteLine("===============");
        
        // define different filter combinations
        var filterConfigurations = new Dictionary<string, Func<ITokenFilter[]>>
        {
            { "No Filters", () => Array.Empty<ITokenFilter>() },
            { "StopWord Filter", () => new ITokenFilter[] { new StopWordFilter(StopWords) } },
            { "Porter Stemmer", () => new ITokenFilter[] { new PorterStemFilter(new EnglishPorter2Stemmer()) } },
            { "Stem+Keep Original", () => new ITokenFilter[] { new StemAndKeepOriginalFilter(new EnglishPorter2Stemmer()) } },
            { "Synonym Filter", () => new ITokenFilter[] { new SynonymFilter(Synonyms) } },
            { "StopWord+Porter", () => new ITokenFilter[] { 
                new StopWordFilter(StopWords), 
                new PorterStemFilter(new EnglishPorter2Stemmer()) } 
            },
            { "Porter+Synonym", () => new ITokenFilter[] { 
                new PorterStemFilter(new EnglishPorter2Stemmer()),
                new SynonymFilter(Synonyms) } 
            },
            { "StopWord+Synonym", () => new ITokenFilter[] { 
                new StopWordFilter(StopWords), 
                new SynonymFilter(Synonyms) } 
            },
            { "StopWord+Stem+Keep", () => new ITokenFilter[] { 
                new StopWordFilter(StopWords), 
                new StemAndKeepOriginalFilter(new EnglishPorter2Stemmer()) } 
            },
            { "All Filters", () => new ITokenFilter[] { 
                new StopWordFilter(StopWords), 
                new PorterStemFilter(new EnglishPorter2Stemmer()),
                new SynonymFilter(Synonyms) } 
            }
        };
        
        var results = new List<(string FileSize, string FilterConfig, int TotalTokens, int UniqueTokens, long TrieMemory, long InvertedIndexMemory, long BloomFilterMemory, long TotalMemory)>();
        
        foreach (var fileSize in _fileSizes)
        {
            var filePath = Path.Combine(_basePath, $"{fileSize}.txt");
            
            try
            {
                Console.WriteLine($"\n===== Processing {fileSize} file =====");
                
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"File not found at {filePath}, skipping...");
                    continue;
                }
                
                string content = File.ReadAllText(filePath);
                Console.WriteLine($"File loaded, length: {content.Length} characters");
                
                foreach (var config in filterConfigurations)
                {
                    Console.WriteLine($"\n--- {config.Key} ---");
                    
                    try 
                    {
                        var filters = config.Value();
                        var analyzer = new Analyzer(new MinimalTokenizer(), filters);
                        
                        var tokens = analyzer.Analyze(content).ToList();
                        int totalTokens = tokens.Count;
                        int uniqueTokens = tokens.Select(t => t.Term).Distinct().Count();
                        
                        Console.WriteLine($"Total tokens: {totalTokens}");
                        Console.WriteLine($"Unique tokens: {uniqueTokens}");
                        
                        var trie = new CompactTrieIndex();
                        trie.AddDocument(1, tokens);
                        long trieMemory = CalculateTrieMemory(trie);
                        Console.WriteLine($"Trie memory: {FormatBytes(trieMemory)}");
                        
                        var invertedIndex = new SimpleInvertedIndex();
                        invertedIndex.AddDocument(1, tokens);
                        long invertedIndexMemory = CalculateInvertedIndexMemory(invertedIndex);
                        Console.WriteLine($"Inverted Index memory: {FormatBytes(invertedIndexMemory)}");

                        var bloomFilter = new BloomFilter(2000000, 0.01);
                        foreach (var token in tokens)
                        {
                            bloomFilter.Add(token.Term);
                        }
                        long bloomFilterMemory = CalculateBloomFilterMemory(bloomFilter);
                        Console.WriteLine($"Bloom Filter memory: {FormatBytes(bloomFilterMemory)}");
                        
                        long totalMemory = trieMemory + invertedIndexMemory + bloomFilterMemory;
                        Console.WriteLine($"Total memory: {FormatBytes(totalMemory)}");
                        
                        results.Add((fileSize, config.Key, totalTokens, uniqueTokens, trieMemory, invertedIndexMemory, bloomFilterMemory, totalMemory));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing {config.Key}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file {fileSize}: {ex.Message}");
            }
        }
        
        if (results.Count > 0)
        {
            ExportFilterAnalysisResultsToCsv(results);
            PrintFilterSummaryStatistics(results);
        }
        
        PrintFilterSummaryStatistics(results);
    }
    
    private void ExportFilterAnalysisResultsToCsv(List<(string FileSize, string FilterConfig, int TotalTokens, int UniqueTokens, long TrieMemory, long InvertedIndexMemory, long BloomFilterMemory, long TotalMemory)> results)
    {
        string csvFileName = $"filter_analysis_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        using (var writer = new StreamWriter(csvFileName))
        {
            writer.WriteLine("File Size,Filter Configuration,Total Tokens,Unique Tokens,Trie Memory (Bytes),Trie Memory (Human),Inverted Index Memory (Bytes),Inverted Index Memory (Human),Bloom Filter Memory (Bytes),Bloom Filter Memory (Human),Total Memory (Bytes),Total Memory (Human)");
            
            foreach (var result in results)
            {
                writer.WriteLine(
                    $"{result.FileSize}," +
                    $"\"{result.FilterConfig}\"," +
                    $"{result.TotalTokens},{result.UniqueTokens}," +
                    $"{result.TrieMemory},{FormatBytes(result.TrieMemory)}," +
                    $"{result.InvertedIndexMemory},{FormatBytes(result.InvertedIndexMemory)}," +
                    $"{result.BloomFilterMemory},{FormatBytes(result.BloomFilterMemory)}," +
                    $"{result.TotalMemory},{FormatBytes(result.TotalMemory)}"
                );
            }
        }
        
        Console.WriteLine($"\nFilter analysis results exported to {Path.GetFullPath(csvFileName)}");
    }
    
    private void PrintFilterSummaryStatistics(List<(string FileSize, string FilterConfig, int TotalTokens, int UniqueTokens, long TrieMemory, long InvertedIndexMemory, long BloomFilterMemory, long TotalMemory)> results)
    {
        Console.WriteLine("\n\n========== FILTER ANALYSIS SUMMARY ==========");
        var byFileSize = results.GroupBy(r => r.FileSize);
        
        foreach (var fileGroup in byFileSize)
        {
            Console.WriteLine($"\nFile Size: {fileGroup.Key}");
            Console.WriteLine("--------------------------------------------");
            Console.WriteLine("Filter Configuration | Token Reduction (%) | Memory Saving (%)");
            Console.WriteLine("--------------------------------------------");
            var baseline = fileGroup.FirstOrDefault(r => r.FilterConfig == "No Filters");
            if (baseline == default)
            {
                Console.WriteLine("Baseline data not found for this file size");
                continue;
            }
        
            foreach (var result in fileGroup)
            {
                double tokenReduction = 100.0 * (1 - (double)result.TotalTokens / baseline.TotalTokens);
                double memorySaving = 100.0 * (1 - (double)result.TotalMemory / baseline.TotalMemory);
                
                Console.WriteLine($"{result.FilterConfig,-20} | {tokenReduction,18:F2} | {memorySaving,16:F2}");
            }
        }
        
        Console.WriteLine("\n\nAVERAGE IMPACT ACROSS ALL FILE SIZES");
        Console.WriteLine("--------------------------------------------");
        Console.WriteLine("Filter Configuration | Avg Token Reduction | Avg Memory Saving");
        Console.WriteLine("--------------------------------------------");
        
        var byFilterConfig = results.GroupBy(r => r.FilterConfig);
        
        foreach (var filterGroup in byFilterConfig)
        {
            if (filterGroup.Key == "No Filters") continue;
            
            double sumTokenReduction = 0;
            double sumMemorySaving = 0;
            int count = 0;
            
            foreach (var fileSize in byFileSize.Select(g => g.Key))
            {
                var filterResult = filterGroup.FirstOrDefault(r => r.FileSize == fileSize);
                var baselineResult = results.FirstOrDefault(r => r.FileSize == fileSize && r.FilterConfig == "No Filters");
                
                if (filterResult != default && baselineResult != default)
                {
                    double tokenReduction = 100.0 * (1 - (double)filterResult.TotalTokens / baselineResult.TotalTokens);
                    double memorySaving = 100.0 * (1 - (double)filterResult.TotalMemory / baselineResult.TotalMemory);
                    
                    sumTokenReduction += tokenReduction;
                    sumMemorySaving += memorySaving;
                    count++;
                }
            }
            
            if (count > 0)
            {
                double avgTokenReduction = sumTokenReduction / count;
                double avgMemorySaving = sumMemorySaving / count;
                
                Console.WriteLine($"{filterGroup.Key,-20} | {avgTokenReduction,18:F2}% | {avgMemorySaving,16:F2}%");
            }
        }
    }
    
    private long CalculateTrieMemory(CompactTrieIndex trie)
    {
        // constants for object overhead
        const int OBJECT_OVERHEAD = 16; // object header (12) + padding (4)
        const int LIST_OVERHEAD = 32;   // object header + internal array reference + size fields
        const int DICT_OVERHEAD = 48;   // dictionary overhead
        
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
        long wordPoolMemory = LIST_OVERHEAD;  // The List object itself
        foreach (var word in trie.WordPool)
        {
            wordPoolMemory += OBJECT_OVERHEAD + (word.Length * sizeof(char));
        }
        long wordToPoolIndexMemory = DICT_OVERHEAD;  // dictionary overhead
        foreach (var kvp in trie.WordToPoolIndex)
        {
            wordToPoolIndexMemory += OBJECT_OVERHEAD + (kvp.Key.Length * sizeof(char)) + sizeof(int);
        }
        
        return totalTrieNodeMemory + wordPoolMemory + wordToPoolIndexMemory;
    }
    
    private long CalculateInvertedIndexMemory(SimpleInvertedIndex invertedIndex)
    {
        // Constants for object overhead
        const int OBJECT_OVERHEAD = 16; // object header (12) + padding (4)
        const int LIST_OVERHEAD = 32;   // object header + internal array reference + size fields
        const int DICT_OVERHEAD = 48;   // dictionary overhead
        
        long mapMemory = DICT_OVERHEAD;

        long postingsMemory = 0;
        
        foreach (var kvp in invertedIndex.Map)
        {
            // String key overhead + content
            mapMemory += OBJECT_OVERHEAD + (kvp.Key.Length * sizeof(char));
            
            // List<Posting> overhead
            postingsMemory += LIST_OVERHEAD;
            
            // Each posting object
            foreach (var posting in kvp.Value)
            {
                // Posting object overhead + DocId + Count fields
                long postingSize = OBJECT_OVERHEAD + sizeof(int) * 2;
                
                // List<int> Positions overhead + content
                postingSize += LIST_OVERHEAD + (posting.Positions.Count * sizeof(int));
                
                postingsMemory += postingSize;
            }
        }
        
        long bitIndexMemory = DICT_OVERHEAD;
        
        foreach (var key in invertedIndex.Map.Keys)
        {

            bitIndexMemory += IntPtr.Size;  
            
            int maxDocId = 0;
            foreach (var posting in invertedIndex.Map[key])
            {
                maxDocId = Math.Max(maxDocId, posting.DocId);
            }
            bitIndexMemory += OBJECT_OVERHEAD + ((maxDocId + 7) / 8);
        }
        
        return mapMemory + postingsMemory + bitIndexMemory;
    }
    
    private long CalculateBloomFilterMemory(BloomFilter bloomFilter)
    {
        const int OBJECT_OVERHEAD = 16; // object header (12) + padding (4)
        
        long bloomFilterObjectMemory = OBJECT_OVERHEAD + sizeof(int); // _hashCount field
        
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
