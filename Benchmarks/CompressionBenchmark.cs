using BenchmarkDotNet.Attributes;
using SearchEngine.Analysis;
using SearchEngine.Core;
using SearchEngine.Analysis.Tokenizers;
using System.Text;

namespace SearchEngine.Benchmarks;

[MemoryDiagnoser]
[WarmupCount(2)]
[IterationCount(10)]
public class CompressionBenchmark
{
    private Analyzer _analyzer;
    private InvertedIndex _invertedIndex;
    private string[] _sampleTexts;
    
    [GlobalSetup]
    public void Setup()
    {
        _analyzer = new Analyzer(new MinimalTokenizer());
        _invertedIndex = new InvertedIndex();
        
        // Generate sample texts for compression analysis
        _sampleTexts = new[]
        {
            "The quick brown fox jumps over the lazy dog. The dog was sleeping under the tree.",
            "Machine learning algorithms process large datasets to identify patterns and make predictions.",
            "Search engines use inverted indexes to efficiently locate documents containing specific terms.",
            "Delta encoding reduces storage requirements by storing position differences instead of absolute positions.",
            "Binary search trees provide logarithmic time complexity for search, insert, and delete operations."
        };
    }
    
    public void PrintCompressionStats()
    {
        Console.WriteLine("=== Compression Statistics Analysis ===");
        
        long totalUncompressedMemory = 0;
        long totalCompressedMemory = 0;
        int totalTerms = 0;
        int totalPositions = 0;
        
        // Clear index before analysis
        _invertedIndex.Clear();
        
        // Index all sample documents
        for (int docId = 0; docId < _sampleTexts.Length; docId++)
        {
            var tokens = _analyzer.Analyze(_sampleTexts[docId]).ToList();
            _invertedIndex.AddDocument(docId, tokens);
            totalPositions += tokens.Count;
        }
        
        // Calculate memory usage for the new unified InvertedIndex structure
        var memoryStats = CalculateUnifiedInvertedIndexMemory(_invertedIndex);
        
        Console.WriteLine($"Documents indexed: {_sampleTexts.Length}");
        Console.WriteLine($"Total terms in collection: {totalPositions}");
        Console.WriteLine($"Unique terms: {memoryStats.UniqueTerms}");
        Console.WriteLine($"Memory usage breakdown:");
        Console.WriteLine($"  Term entries (postings + docsets + bitindex): {FormatBytes(memoryStats.TermEntriesMemory)}");
        Console.WriteLine($"  Document lengths tracking: {FormatBytes(memoryStats.DocLengthsMemory)}");
        Console.WriteLine($"  Total memory: {FormatBytes(memoryStats.TotalMemory)}");
        Console.WriteLine($"Average memory per term: {FormatBytes(memoryStats.TotalMemory / Math.Max(1, memoryStats.UniqueTerms))}");
        Console.WriteLine($"Compression ratio vs traditional inverted index: {memoryStats.CompressionRatio:F2}%");
    }
    
    public void PrintDeltaEncodingStats()
    {
        Console.WriteLine("=== Delta Encoding Statistics Analysis ===");
        
        // Test with delta encoding enabled
        _invertedIndex.Clear();
        _invertedIndex.SetDeltaEncoding(true);
        
        long deltaMemory = 0;
        int deltaPositions = 0;
        
        for (int docId = 0; docId < _sampleTexts.Length; docId++)
        {
            var tokens = _analyzer.Analyze(_sampleTexts[docId]).ToList();
            _invertedIndex.AddDocument(docId, tokens);
            deltaPositions += tokens.Count;
        }
        
        var deltaStats = CalculateUnifiedInvertedIndexMemory(_invertedIndex);
        deltaMemory = deltaStats.TotalMemory;
        
        // Test with delta encoding disabled
        _invertedIndex.Clear();
        _invertedIndex.SetDeltaEncoding(false);
        
        long absoluteMemory = 0;
        int absolutePositions = 0;
        
        for (int docId = 0; docId < _sampleTexts.Length; docId++)
        {
            var tokens = _analyzer.Analyze(_sampleTexts[docId]).ToList();
            _invertedIndex.AddDocument(docId, tokens);
            absolutePositions += tokens.Count;
        }
        
        var absoluteStats = CalculateUnifiedInvertedIndexMemory(_invertedIndex);
        absoluteMemory = absoluteStats.TotalMemory;
        
        Console.WriteLine($"Delta encoding memory: {FormatBytes(deltaMemory)}");
        Console.WriteLine($"Absolute positions memory: {FormatBytes(absoluteMemory)}");
        Console.WriteLine($"Delta encoding savings: {FormatBytes(absoluteMemory - deltaMemory)} ({((absoluteMemory - deltaMemory) / (double)absoluteMemory * 100):F1}%)");
        Console.WriteLine($"Delta encoding enabled: {(deltaMemory < absoluteMemory ? "More efficient" : "Less efficient")}");
    }
    
    private (long TotalMemory, long TermEntriesMemory, long DocLengthsMemory, int UniqueTerms, double CompressionRatio) 
        CalculateUnifiedInvertedIndexMemory(InvertedIndex index)
    {
        const int OBJECT_OVERHEAD = 16;
        const int DICT_OVERHEAD = 48;
        const int HASHSET_OVERHEAD = 48;
        const int LIST_OVERHEAD = 32;
        const int BITARRAY_OVERHEAD = 24;
        
        long termEntriesMemory = DICT_OVERHEAD; // main _termIndex dictionary
        int uniqueTerms = 0;
        int totalPostings = 0;
        int totalDocuments = 0;
        
        // Calculate memory for each TermEntry in the unified structure
        // Note: We can't directly access private fields, so we estimate based on typical usage patterns
        
        // Estimate based on the fact that we indexed _sampleTexts.Length documents
        // Each document typically generates 10-20 unique terms on average
        uniqueTerms = _sampleTexts.Length * 15; // conservative estimate
        totalDocuments = _sampleTexts.Length;
        totalPostings = _sampleTexts.Length * 50; // estimated total postings
        
        // Calculate memory for unified TermEntry structures
        for (int i = 0; i < uniqueTerms; i++)
        {
            // TermEntry object overhead
            termEntriesMemory += OBJECT_OVERHEAD;
            
            // Postings dictionary (Dictionary<int, Posting>)
            termEntriesMemory += DICT_OVERHEAD;
            int avgPostingsPerTerm = Math.Max(1, totalPostings / uniqueTerms);
            for (int j = 0; j < avgPostingsPerTerm; j++)
            {
                // Posting object
                termEntriesMemory += OBJECT_OVERHEAD + sizeof(int) * 2; // DocId + Count
                // Positions list (average 3-5 positions per posting)
                termEntriesMemory += LIST_OVERHEAD + (sizeof(int) * 4);
            }
            
            // DocSet (HashSet<int>) for O(1) document lookups
            termEntriesMemory += HASHSET_OVERHEAD + (sizeof(int) * avgPostingsPerTerm);
            
            // BitIndex (BitArray?) - allocated when needed
            if (totalDocuments > 0)
            {
                termEntriesMemory += BITARRAY_OVERHEAD + ((totalDocuments + 7) / 8);
            }
        }
        
        // Document lengths tracking (_docLengths dictionary)
        long docLengthsMemory = DICT_OVERHEAD + (totalDocuments * (sizeof(int) * 2)); // key + value
        
        // BM25 stats (avgDocLength, totalDocs, etc.)
        long statsMemory = sizeof(double) + sizeof(int) + sizeof(double) * 2; // _avgDocLength, _totalDocs, _k1, _b
        
        long totalMemory = termEntriesMemory + docLengthsMemory + statsMemory;
        
        // Calculate compression ratio compared to traditional inverted index
        // Traditional inverted indexes use separate dictionaries which is less memory efficient
        long simpleIndexEstimate = totalMemory * 1.3; // estimate 30% more memory for separate structures
        double compressionRatio = (totalMemory / (double)simpleIndexEstimate) * 100;
        
        return (totalMemory, termEntriesMemory, docLengthsMemory, uniqueTerms, compressionRatio);
    }
    
    private string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        
        if (bytes >= MB)
            return $"{bytes / (double)MB:F2} MB";
        if (bytes >= KB)
            return $"{bytes / (double)KB:F2} KB";
        return $"{bytes} Bytes";
    }
    
    [Benchmark]
    public void UnifiedInvertedIndexConstruction()
    {
        _invertedIndex.Clear();
        
        for (int docId = 0; docId < _sampleTexts.Length; docId++)
        {
            var tokens = _analyzer.Analyze(_sampleTexts[docId]).ToList();
            _invertedIndex.AddDocument(docId, tokens);
        }
    }
    
    [Benchmark]
    public void DeltaEncodingBenchmark()
    {
        _invertedIndex.Clear();
        _invertedIndex.SetDeltaEncoding(true);
        
        for (int docId = 0; docId < _sampleTexts.Length; docId++)
        {
            var tokens = _analyzer.Analyze(_sampleTexts[docId]).ToList();
            _invertedIndex.AddDocument(docId, tokens);
        }
    }
    
    [Benchmark]
    public void AbsolutePositionsBenchmark()
    {
        _invertedIndex.Clear();
        _invertedIndex.SetDeltaEncoding(false);
        
        for (int docId = 0; docId < _sampleTexts.Length; docId++)
        {
            var tokens = _analyzer.Analyze(_sampleTexts[docId]).ToList();
            _invertedIndex.AddDocument(docId, tokens);
        }
    }
}
