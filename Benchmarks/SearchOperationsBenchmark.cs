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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Tokenizers;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Services;
using SearchEngine.Services.Interfaces;
using SearchEngine.Persistence;
using SearchEngine.Benchmarks;

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
        private ServiceProvider _serviceProvider;
        private IIndexingService _indexingService;
        private ISearchService _searchService;
        private IExactPrefixIndex _trie;
        private IFullTextIndex _invertedIndex; 
        private IBloomFilter _bloomFilter;
        private Analyzer _analyzer;
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
            // Set up dependency injection similar to Program.cs
            var services = new ServiceCollection();
            
            // Configure services like in Program.cs
            services.AddSingleton<Analyzer>(sp => new Analyzer(new MinimalTokenizer()));
            
            // Add search operations
            services.AddSingleton<ISearchOperation, ExactSearchOperation>();
            services.AddSingleton<ISearchOperation>(sp =>
                new PrefixDocsSearchOperation(sp.GetRequiredService<IExactPrefixIndex>())
            );
            services.AddSingleton<ISearchOperation, AutoCompleteSearchOperation>();
            services.AddSingleton<ISearchOperation, FullTextSearchOperation>();
            services.AddSingleton<ISearchOperation, BloomFilterSearchOperation>();
            
            // Add core services
            services.AddSingleton<ISearchService, SearchService>();
            services.AddSingleton<IIndexingService, IndexingService>();
            
            // Register indexes
            services.AddSingleton<IExactPrefixIndex, CompactTrieIndex>();
            services.AddSingleton<IFullTextIndex, CompactTrieIndex>();
            services.AddSingleton<IBloomFilter>(provider => new BloomFilter(8000000, 0.01));
            
            // Add logging (using null logger for benchmarks)
            services.AddLogging(builder => {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Warning); // Only show warnings and above
            });
            
            // Add repository services for benchmarking
            // Use interfaces directly to avoid issues with non-virtual methods
            services.AddSingleton<DocumentRepository, DocumentRepository>();
            services.AddSingleton<DocumentTermRepository, DocumentTermRepository>();
            services.AddSingleton<IDocumentService, DocumentService>();
            services.AddSingleton<FileContentService>();
            
            _serviceProvider = services.BuildServiceProvider();
            
            _serviceProvider = services.BuildServiceProvider();
            
            // Get services
            _indexingService = _serviceProvider.GetRequiredService<IIndexingService>();
            _searchService = _serviceProvider.GetRequiredService<ISearchService>();
            _trie = _serviceProvider.GetRequiredService<IExactPrefixIndex>();
            _invertedIndex = _serviceProvider.GetRequiredService<IFullTextIndex>();
            _bloomFilter = _serviceProvider.GetRequiredService<IBloomFilter>();
            _analyzer = _serviceProvider.GetRequiredService<Analyzer>();
            
            // Turn off BM25 for benchmarks
            if (_trie is CompactTrieIndex trieIndex)
            {
                trieIndex.SetUseBM25(false);
            }
            
            _currentFile = Path.Combine(_basePath, $"{FileSize}.txt");

            if (Environment.GetEnvironmentVariable("BENCHMARK_VERBOSE") == "1")
                Console.WriteLine($"Loading file: {_currentFile}");

            _documentsIndexed = 0;
            _tokensProcessed = 0;
            ProcessMultipleDocumentsWithService().Wait();

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
            _indexingService = null;
            _searchService = null;
            
            // Dispose of the service provider
            (_serviceProvider as IDisposable)?.Dispose();
            _serviceProvider = null;
            
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

        private async Task ProcessMultipleDocumentsWithService()
        {
            // Prepare for batch processing similar to Program.cs
            var documentBatch = new List<(int docId, string content)>();
            const int batchSize = 100;
            
            using var reader = new StreamReader(_currentFile, Encoding.UTF8);
            string? line;
            string? currentTitle = null;
            var sb = new StringBuilder();
            int docId = 1;
            
            // Get required services
            var docService = _serviceProvider.GetRequiredService<IDocumentService>();
            var indexingService = _serviceProvider.GetRequiredService<IIndexingService>();
            
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
                        string content = sb.ToString().Trim();
                        
                        // Create document with content
                        docId = await docService.CreateWithContentAsync(currentTitle ?? "Untitled", content);
                        
                        // Add to batch for indexing
                        documentBatch.Add((docId, content));
                        
                        // Process batch when it reaches the target size
                        if (documentBatch.Count >= batchSize)
                        {
                            await indexingService.IndexDocumentsBatchAsync(documentBatch);
                            documentBatch.Clear();
                            
                            if (Environment.GetEnvironmentVariable("BENCHMARK_VERBOSE") == "1")
                                Console.WriteLine($"Processed batch of {batchSize} documents");
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
                await indexingService.IndexDocumentsBatchAsync(documentBatch);
                if (Environment.GetEnvironmentVariable("BENCHMARK_VERBOSE") == "1")
                    Console.WriteLine($"Processed final batch of {documentBatch.Count} documents");
            }
            
            // Get document count
            var docs = await docService.GetAllAsync();
            _documentsIndexed = docs.Count;
            
            // We don't need to calculate token count since it's not used in the benchmarks
            _tokensProcessed = 0;
        }
    }
}
