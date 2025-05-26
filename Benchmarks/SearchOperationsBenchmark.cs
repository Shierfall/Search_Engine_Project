using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using SearchEngine.Analysis;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Analysis.Tokenizers;
using System.Text;

namespace SearchEngine.Benchmarks;

[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(20)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CsvExporter]
public class SearchOperationsBenchmark
{
    private string[] _fileSizes = new[] { "100KB", "1MB", "2MB", "5MB", "10MB", "20MB", "50MB", "100MB", "200MB", "400MB" };
    private string _basePath = "/zhome/6b/1/188023/Downloads/texts/"; // path to text files
    private Analyzer _analyzer = null!;
    private IExactPrefixIndex _trie = null!;
    private IFullTextIndex _invertedIndex = null!;
    private IBloomFilter _bloomFilter = null!;
    private string _currentFile = null!;
    
    // categorize queries by type for more meaningful benchmarks
    private static readonly Dictionary<string, List<string>> _queryCategories = new()
    {
        ["Exact"] = new() { "and", "or", "cat", "bread" },
        ["Prefix"] = new() { "an*", "or*", "ca*", "br*" },
        ["Phrase"] = new() { "he was", "it was a", "and when", "or when" },
        ["Boolean"] = new() { "and && or || cat", "cat && bread", "bread && cat || or", "or && and" }
    };

    [ParamsSource(nameof(FileSizes))]
    public string FileSize { get; set; } = null!;

    public IEnumerable<string> FileSizes => _fileSizes;

    [GlobalSetup]
    public void Setup()
    {
        _analyzer = new Analyzer(new MinimalTokenizer());
        _trie = new CompactTrieIndex();
        _invertedIndex = new SimpleInvertedIndex();
        _bloomFilter = new BloomFilter(2000000, 0.03);
        _currentFile = Path.Combine(_basePath, $"{FileSize}.txt");
        
        Console.WriteLine($"Loading file: {_currentFile}");
        
        ProcessMultipleDocuments();
        
        Console.WriteLine("Benchmark setup complete.");
    }
    
    private void ProcessMultipleDocuments()
    {
        using var reader = new StreamReader(_currentFile, Encoding.UTF8);
        string? line;
        string? currentTitle = null;
        var sb = new StringBuilder();
        int docId = 1;
        int totalTokens = 0;
        
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
                    var tokens = _analyzer.Analyze(content).ToList();
                    totalTokens += tokens.Count;
                    _trie.AddDocument(docId, tokens);
                    _invertedIndex.AddDocument(docId, tokens);
                    
                    foreach (var token in tokens)
                    {
                        _bloomFilter.Add(token.Term);
                    }
                    
                    docId++;
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
            var tokens = _analyzer.Analyze(content).ToList();
            totalTokens += tokens.Count;
            
            _trie.AddDocument(docId, tokens);
            _invertedIndex.AddDocument(docId, tokens);
            
            foreach (var token in tokens)
            {
                _bloomFilter.Add(token.Term);
            }
        }
        
        Console.WriteLine($"Total documents indexed: {docId}");
        Console.WriteLine($"Total tokens generated: {totalTokens}");
    }


    [BenchmarkCategory("ExactSearch")]
    [Arguments("and")]
    [Arguments("or")]
    [Arguments("cat")]
    [Arguments("bread")]
    [Benchmark(Description = "Trie-Exact")]
    public List<(int docId, int count)> TrieExactSearch(string query)
    {
        return _trie.ExactSearchDocuments(query);
    }

    [BenchmarkCategory("ExactSearch")]
    [Arguments("and")]
    [Arguments("or")]
    [Arguments("cat")]
    [Arguments("bread")]
    [Benchmark(Description = "InvertedIndex-Exact")]
    public List<(int docId, int count)> InvertedIndexExactSearch(string query)
    {
        return _invertedIndex.ExactSearch(query);
    }

    [BenchmarkCategory("ExactSearch")]
    [Arguments("and")]
    [Arguments("or")]
    [Arguments("cat")]
    [Arguments("bread")]
    [Benchmark(Description = "BloomFilter-Exact")]
    public bool BloomFilterExactSearch(string query)
    {
        return _bloomFilter.MightContain(query);
    }

    // prefix search benchmarks
    [BenchmarkCategory("PrefixSearch")]
    [Arguments("an")]
    [Arguments("or")]
    [Arguments("ca")]
    [Arguments("br")]
    [Benchmark(Description = "Trie-PrefixDocuments")]
    public List<int> TriePrefixSearchDocuments(string query)
    {
        return _trie.PrefixSearchDocuments(query);
    }

    [BenchmarkCategory("PrefixSearch")]
    [Arguments("an")]
    [Arguments("or")]
    [Arguments("ca")]
    [Arguments("br")]
    [Benchmark(Description = "Trie-PrefixWords")]
    public List<(string word, List<int> docIds)> TriePrefixSearch(string query)
    {
        return _trie.PrefixSearch(query);
    }

    [BenchmarkCategory("PrefixSearch")]
    [Arguments("an")]
    [Arguments("or")]
    [Arguments("ca")]
    [Arguments("br")]
    [Benchmark(Description = "InvertedIndex-Prefix")]
    public List<(string word, List<int> docIds)> InvertedIndexPrefixSearch(string query)
    {
        return _invertedIndex.PrefixSearch(query);
    }

    // phrase search benchmarks - only for InvertedIndex, as requested
    [BenchmarkCategory("PhraseSearch")]
    [Arguments("he was")]
    [Arguments("it was")]
    [Arguments("and it was")]
    [Arguments("and it")]
    [Benchmark(Description = "InvertedIndex-Phrase")]
    public List<(int docId, int count)> InvertedIndexPhraseSearch(string query)
    {
        return _invertedIndex.PhraseSearch(query);
    }

    // boolean search benchmarks
    [BenchmarkCategory("BooleanSearch")]
    [Arguments("and && or || cat")]
    [Arguments("cat || or && and || it")]
    [Arguments("and || or || bread || or")]
    [Arguments("cat || bread")]
    [Benchmark(Description = "Trie-Boolean-Naive")]
    public List<(int docId, int count)> TrieBooleanSearchNaive(string query)
    {
        return _trie.BooleanSearchNaive(query);
    }

    [BenchmarkCategory("BooleanSearch")]
    [Arguments("and && or || cat")]
    [Arguments("cat || or && and || it")]
    [Arguments("and || or || bread || or")]
    [Arguments("cat || bread")]
    [Benchmark(Description = "InvertedIndex-Boolean-Naive")]
    public List<(int docId, int count)> InvertedIndexBooleanSearchNaive(string query)
    {
        return _invertedIndex.BooleanSearchNaive(query);
    }

    [BenchmarkCategory("BooleanSearch")]
    [Arguments("and && or || cat")]
    [Arguments("cat || or && and || it")]
    [Arguments("and || or || bread || or")]
    [Arguments("cat || bread")]
    [Benchmark(Description = "InvertedIndex-Boolean-Bitset")]
    public List<(int docId, int count)> InvertedIndexBooleanSearchBitset(string query)
    {
        return _invertedIndex.BooleanSearch(query);
    }
} 