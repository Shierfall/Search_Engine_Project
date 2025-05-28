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
        _invertedIndex = new SimpleInvertedIndex();
        _bloomFilter = new BloomFilter(2000000, 0.03); // assuming max 1M unique terms
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
        _trie = null!;
        _invertedIndex = null!;
        _bloomFilter = null!;
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

        // Force garbage collection to get a clean state
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Get initial memory
        long initialMemory = GC.GetTotalMemory(true);

        // measure Trie memory with BM25 disabled
        var trieTokens = _analyzer.Analyze(_currentContent).ToList();
        var compactTrie = new CompactTrieIndex();
        compactTrie.SetUseBM25(false); // disable BM25 for fair comparison
        compactTrie.AddDocument(1, trieTokens);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long trieMemory = GC.GetTotalMemory(true) - initialMemory;
        Console.WriteLine($"Trie Index (no BM25): {FormatBytes(trieMemory)}");

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