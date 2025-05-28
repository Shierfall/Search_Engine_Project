using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Tokenizers;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;

namespace SearchEngine.Benchmarks
{
    [MemoryDiagnoser]
    [WarmupCount(4)]
    [IterationCount(20)]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CsvExporter]
    public class SearchOperationsBenchmark
    {
        private string[] _fileSizes = new[] { "100KB", "1MB", "2MB", "5MB", "10MB", "20MB", "50MB", "100MB", "200MB", "400MB" };
        private string _basePath = "/zhome/6b/1/188023/Downloads/texts/";
        private Analyzer _analyzer;
        private IExactPrefixIndex _trie;
        private IFullTextIndex _invertedIndex;
        private IBloomFilter _bloomFilter;
        private string _currentFile;
        private int _documentsIndexed;
        private int _tokensProcessed;

        private static readonly Dictionary<string, List<string>> _queryCategories = new()
        {
            ["Exact"] = new() { "and", "or", "cat", "because" },
            ["Prefix"] = new() { "an*", "or*", "ca*", "br*" },
            ["Phrase"] = new() { "he was", "it was a", "and when", "or when" },
            ["Boolean"] = new() { "and && or || cat", "cat && because", "because && cat || or", "or && and" }
        };

        [ParamsSource(nameof(FileSizes))]
        public string FileSize { get; set; }

        public IEnumerable<string> FileSizes => _fileSizes;

        [GlobalSetup]
        public void Setup()
        {
            _analyzer = new Analyzer(new MinimalTokenizer());
            _trie = new CompactTrieIndex();
            _trie.SetUseBM25(false);
            _invertedIndex = new InvertedIndex();
            _bloomFilter = new BloomFilter(8000000, 0.01);
            _currentFile = Path.Combine(_basePath, $"{FileSize}.txt");

            if (Environment.GetEnvironmentVariable("BENCHMARK_VERBOSE") == "1")
                Console.WriteLine($"Loading file: {_currentFile}");

            _documentsIndexed = 0;
            _tokensProcessed = 0;
            ProcessMultipleDocuments();

            if (Environment.GetEnvironmentVariable("BENCHMARK_VERBOSE") == "1")
                Console.WriteLine($"Benchmark setup complete. Documents: {_documentsIndexed}, Tokens: {_tokensProcessed}");

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

        // Exact Search Benchmarks
        [BenchmarkCategory("ExactSearch")]
        [Arguments("and")]
        [Arguments("or")]
        [Arguments("cat")]
        [Arguments("because")]
        [Benchmark(Description = "Trie-Exact")]
        public List<(int docId, int count)> TrieExactSearch(string query) => _trie.ExactSearchDocuments(query);

        [BenchmarkCategory("ExactSearch")]
        [Arguments("and")]
        [Arguments("or")]
        [Arguments("cat")]
        [Arguments("because")]
        [Benchmark(Description = "InvertedIndex-Exact")]
        public List<(int docId, int count)> InvertedIndexExactSearch(string query) => _invertedIndex.ExactSearch(query);

        [BenchmarkCategory("ExactSearch")]
        [Arguments("and")]
        [Arguments("or")]
        [Arguments("cat")]
        [Arguments("because")]
        [Benchmark(Description = "BloomFilter-Exact")]
        public bool BloomFilterExactSearch(string query) => _bloomFilter.MightContain(query);

        // Prefix Search Benchmarks
        [BenchmarkCategory("PrefixSearch")]
        [Arguments("an")]
        [Arguments("or")]
        [Arguments("ca")]
        [Arguments("br")]
        [Benchmark(Description = "Trie-PrefixDocuments")]
        public List<int> TriePrefixSearchDocuments(string query) => _trie.PrefixSearchDocuments(query);

        [BenchmarkCategory("PrefixSearch")]
        [Arguments("an")]
        [Arguments("or")]
        [Arguments("ca")]
        [Arguments("br")]
        [Benchmark(Description = "Trie-PrefixWords")]
        public List<(string word, List<int> docIds)> TriePrefixSearch(string query) => _trie.PrefixSearch(query);

        [BenchmarkCategory("PrefixSearch")]
        [Arguments("an")]
        [Arguments("or")]
        [Arguments("ca")]
        [Arguments("br")]
        [Benchmark(Description = "InvertedIndex-Prefix")]
        public List<(string word, List<int> docIds)> InvertedIndexPrefixSearch(string query) => _invertedIndex.PrefixSearch(query);

        // Phrase Search Benchmarks
        [BenchmarkCategory("PhraseSearch")]
        [Arguments("he was")]
        [Arguments("it was")]
        [Arguments("and it was")]
        [Arguments("and it")]
        [Benchmark(Description = "InvertedIndex-Phrase")]
        public List<(int docId, int count)> InvertedIndexPhraseSearch(string query) => _invertedIndex.PhraseSearch(query);

        [BenchmarkCategory("PhraseSearch")]
        [Arguments("he was")]
        [Arguments("it was")]
        [Arguments("and it was")]
        [Arguments("and it")]
        [Benchmark(Description = "Trie-Phrase")]
        public List<(int docId, int count)> TriePhraseSearch(string query) => ((CompactTrieIndex)_trie).PhraseSearch(query);

        // Boolean Search Benchmarks
        [BenchmarkCategory("BooleanSearch")]
        [Arguments("and && or || cat")]
        [Arguments("cat || or && and || it")]
        [Arguments("and || or || because || or")]
        [Arguments("cat || because")]
        [Benchmark(Description = "Trie-Boolean-Naive")]
        public List<(int docId, int count)> TrieBooleanSearchNaive(string query) => ((CompactTrieIndex)_trie).BooleanSearchNaive(query);

        [BenchmarkCategory("BooleanSearch")]
        [Arguments("and && or || cat")]
        [Arguments("cat || or && and || it")]
        [Arguments("and || or || because || or")]
        [Arguments("cat || because")]
        [Benchmark(Description = "Trie-Boolean-Bitset")]
        public List<(int docId, int count)> TrieBooleanSearch(string query) => ((CompactTrieIndex)_trie).BooleanSearch(query);

        [BenchmarkCategory("BooleanSearch")]
        [Arguments("and && or || cat")]
        [Arguments("cat || or && and || it")]
        [Arguments("and || or || because || or")]
        [Arguments("cat || because")]
        [Benchmark(Description = "InvertedIndex-Boolean-Naive")]
        public List<(int docId, int count)> InvertedIndexBooleanSearchNaive(string query) => _invertedIndex.BooleanSearchNaive(query);

        [BenchmarkCategory("BooleanSearch")]
        [Arguments("and && or || cat")]
        [Arguments("cat || or && and || it")]
        [Arguments("and || or || because || or")]
        [Arguments("cat || because")]
        [Benchmark(Description = "InvertedIndex-Boolean-Bitset")]
        public List<(int docId, int count)> InvertedIndexBooleanSearch(string query) => _invertedIndex.BooleanSearch(query);

        private void ProcessMultipleDocuments()
        {
            var documents = new List<(int docId, string content)>();
            using var reader = new StreamReader(_currentFile, Encoding.UTF8);
            string? line;
            string? currentTitle = null;
            var sb = new StringBuilder();
            int docId = 1;

            var documentBatch = new List<(int docId, List<Token> tokens)>();
            var batchSize = 100;
            int totalTokens = 0;

            while ((line = reader.ReadLine()) != null)
            {
                if (currentTitle == null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        currentTitle = line;
                }
                else if (line.Trim() == "---END.OF.DOCUMENT---")
                {
                    if (sb.Length > 0)
                    {
                        var content = sb.ToString().Trim();
                        var tokens = _analyzer.Analyze(content).ToList();
                        totalTokens += tokens.Count;
                        documentBatch.Add((docId, tokens));
                        docId++;

                        // Process batch when it reaches the target size
                        if (documentBatch.Count >= batchSize)
                        {
                            Parallel.ForEach(documentBatch, doc =>
                            {
                                _trie.AddDocument(doc.docId, doc.tokens);
                                _invertedIndex.AddDocument(doc.docId, doc.tokens);
                                foreach (var token in doc.tokens)
                                    _bloomFilter.Add(token.Term);
                            });

                            documentBatch.Clear();
                        }
                    }
                    currentTitle = null;
                    sb.Clear();
                }
                else
                {
                    sb.AppendLine(line);
                }
            }

            // Process final batch if any documents remain
            if (documentBatch.Count > 0)
            {
                Parallel.ForEach(documentBatch, doc =>
                {
                    _trie.AddDocument(doc.docId, doc.tokens);
                    _invertedIndex.AddDocument(doc.docId, doc.tokens);
                    foreach (var token in doc.tokens)
                        _bloomFilter.Add(token.Term);
                });
            }

            _documentsIndexed = docId - 1;
            _tokensProcessed = totalTokens;
        }
    }
}
