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
    private string _basePath = "/home/shierfall/Downloads/texts/";
    private Analyzer _analyzer;
    private IExactPrefixIndex _trie;
    private IFullTextIndex _invertedIndex;
    private IBloomFilter _bloomFilter;
    private string _currentFile;
    private string _currentContent;

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
        _currentContent = File.ReadAllText(_currentFile);
    }

    [Benchmark]
    public void TrieConstruction()
    {
        var tokens = _analyzer.Analyze(_currentContent).ToList();
        _trie.AddDocument(1, tokens);
    }

    [Benchmark]
    public void InvertedIndexConstruction()
    {
        var tokens = _analyzer.Analyze(_currentContent).ToList();
        _invertedIndex.AddDocument(1, tokens);
    }

    [Benchmark]
    public void BloomFilterConstruction()
    {
        var tokens = _analyzer.Analyze(_currentContent).ToList();
        foreach (var token in tokens)
        {
            _bloomFilter.Add(token.Term);
        }
    }

    public void PrintMemoryUsage()
    {
        Console.WriteLine($"\nMemory Usage for {FileSize} file:");
        Console.WriteLine("----------------------------------------");

        // Force garbage collection to get a clean state
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Get initial memory
        long initialMemory = GC.GetTotalMemory(true);

        // Measure Trie memory
        var trieTokens = _analyzer.Analyze(_currentContent).ToList();
        _trie = new CompactTrieIndex();
        _trie.AddDocument(1, trieTokens);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long trieMemory = GC.GetTotalMemory(true) - initialMemory;
        Console.WriteLine($"Trie Index: {FormatBytes(trieMemory)}");

        // Measure Inverted Index memory
        initialMemory = GC.GetTotalMemory(true);
        _invertedIndex = new SimpleInvertedIndex();
        _invertedIndex.AddDocument(1, trieTokens);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long invertedIndexMemory = GC.GetTotalMemory(true) - initialMemory;
        Console.WriteLine($"Inverted Index: {FormatBytes(invertedIndexMemory)}");

        // Measure Bloom Filter memory
        initialMemory = GC.GetTotalMemory(true);
        _bloomFilter = new BloomFilter(1000000, 0.01);
        foreach (var token in trieTokens)
        {
            _bloomFilter.Add(token.Term);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long bloomFilterMemory = GC.GetTotalMemory(true) - initialMemory;
        Console.WriteLine($"Bloom Filter: {FormatBytes(bloomFilterMemory)}");

        // Print total memory
        Console.WriteLine($"Total Memory: {FormatBytes(trieMemory + invertedIndexMemory + bloomFilterMemory)}");
        Console.WriteLine("----------------------------------------\n");
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