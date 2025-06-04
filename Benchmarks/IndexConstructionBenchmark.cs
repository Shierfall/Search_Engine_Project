// IndexConstructionBenchmark.cs
using BenchmarkDotNet.Attributes;
using SearchEngine.Analysis;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Analysis.Tokenizers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SearchEngine.Benchmarks
{
    [MemoryDiagnoser]
    [WarmupCount(1)]
    [IterationCount(4)]
    public class IndexConstructionBenchmark
    {
        private string[] _fileSizes = new[] {
            "100KB", "1MB", "2MB", "5MB", "10MB",
            "20MB", "50MB", "100MB", "200MB",
            "400MB", "800MB", "1700MB"
        };
        private string _basePath = "/zhome/6b/1/188023/Downloads/texts/"; // adjust to your environment
        private Analyzer _analyzer;
        private IExactPrefixIndex _trie;
        private IFullTextIndex _invertedIndex;
        private IBloomFilter _bloomFilter;
        private string _currentFile;
        private int _documentsIndexed;
        private int _tokensProcessed;

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
            {
                Console.WriteLine($"Loading file: {_currentFile}");
            }

            // Force a clean GC before any indexing
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _documentsIndexed = 0;
            _tokensProcessed = 0;
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

            // Re-create trie and measure its memory usage
            _trie = new CompactTrieIndex();
            _trie.SetUseBM25(false);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long beforeBytes = GC.GetTotalMemory(true);

            ProcessMultipleDocuments(doc =>
            {
                var tokens = _analyzer.Analyze(doc.content).ToList();
                _trie.AddDocument(doc.id, tokens);
                docCount++;
                tokenCount += tokens.Count;
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long afterBytes = GC.GetTotalMemory(true);

            _documentsIndexed = docCount;
            _tokensProcessed = tokenCount;

            long usedBytes = afterBytes - beforeBytes;
            Console.WriteLine($"[Trie] Documents indexed: {docCount}, Tokens: {tokenCount}, Memory used: {FormatBytes(usedBytes)}");
        }

        [Benchmark]
        public void InvertedIndexConstruction()
        {
            int docCount = 0;
            int tokenCount = 0;

            // Re-create inverted index and measure its memory usage
            _invertedIndex = new InvertedIndex();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long beforeBytes = GC.GetTotalMemory(true);

            ProcessMultipleDocuments(doc =>
            {
                var tokens = _analyzer.Analyze(doc.content).ToList();
                _invertedIndex.AddDocument(doc.id, tokens);
                docCount++;
                tokenCount += tokens.Count;
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long afterBytes = GC.GetTotalMemory(true);

            _documentsIndexed = docCount;
            _tokensProcessed = tokenCount;

            long usedBytes = afterBytes - beforeBytes;
            Console.WriteLine($"[InvertedIndex] Documents indexed: {docCount}, Tokens: {tokenCount}, Memory used: {FormatBytes(usedBytes)}");
        }

        [Benchmark]
        public void BloomFilterConstruction()
        {
            int docCount = 0;
            int tokenCount = 0;

            // Re-create bloom filter and measure its memory usage
            _bloomFilter = new BloomFilter(8000000, 0.01);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long beforeBytes = GC.GetTotalMemory(true);

            ProcessMultipleDocuments(doc =>
            {
                var tokens = _analyzer.Analyze(doc.content).ToList();
                foreach (var token in tokens)
                {
                    _bloomFilter.Add(token.Term);
                }
                docCount++;
                tokenCount += tokens.Count;
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long afterBytes = GC.GetTotalMemory(true);

            _documentsIndexed = docCount;
            _tokensProcessed = tokenCount;

            long usedBytes = afterBytes - beforeBytes;
            Console.WriteLine($"[BloomFilter] Documents indexed: {docCount}, Tokens: {tokenCount}, Memory used: {FormatBytes(usedBytes)}");
        }

        /// <summary>
        /// Reads the current file, splits by document markers, and invokes the given action for each document.
        /// </summary>
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
                    sb.AppendLine(line);
                }
            }

            // Process last document if file does not end with marker
            if (currentTitle != null && sb.Length > 0)
            {
                processDocument((docId, sb.ToString().Trim()));
            }
        }

        /// <summary>
        /// Prints memory usage for each index (GC.GetTotalMemory-based) and exports to CSV.
        /// </summary>
        public void PrintMemoryUsage()
        {
            Console.WriteLine($"\nMemory Usage for {FileSize} file:");
            Console.WriteLine("----------------------------------------");

            // 1. Measure Trie
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long beforeTrie = GC.GetTotalMemory(true);

            var trie = new CompactTrieIndex();
            trie.SetUseBM25(false);
            ProcessMultipleDocuments(doc =>
            {
                var tokens = _analyzer.Analyze(doc.content).ToList();
                trie.AddDocument(doc.id, tokens);
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long afterTrie = GC.GetTotalMemory(true);
            long trieMemory = afterTrie - beforeTrie;
            Console.WriteLine($"Trie Index memory: {FormatBytes(trieMemory)}");

            // 2. Measure Inverted Index
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long beforeInv = GC.GetTotalMemory(true);

            var inverted = new InvertedIndex();
            ProcessMultipleDocuments(doc =>
            {
                var tokens = _analyzer.Analyze(doc.content).ToList();
                inverted.AddDocument(doc.id, tokens);
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long afterInv = GC.GetTotalMemory(true);
            long invertedMemory = afterInv - beforeInv;
            Console.WriteLine($"Inverted Index memory: {FormatBytes(invertedMemory)}");

            // 3. Measure Bloom Filter
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long beforeBloom = GC.GetTotalMemory(true);

            var bloom = new BloomFilter(8000000, 0.01);
            ProcessMultipleDocuments(doc =>
            {
                var tokens = _analyzer.Analyze(doc.content).ToList();
                foreach (var token in tokens)
                {
                    bloom.Add(token.Term);
                }
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long afterBloom = GC.GetTotalMemory(true);
            long bloomMemory = afterBloom - beforeBloom;
            Console.WriteLine($"Bloom Filter memory: {FormatBytes(bloomMemory)}");

            // Export to CSV
            ExportMemoryUsageToCsv(FileSize, 0, 0, trieMemory, invertedMemory, bloomMemory);

            Console.WriteLine("----------------------------------------\n");
        }

        private void ExportMemoryUsageToCsv(
            string fileSize,
            int totalTokens,
            int uniqueTokens,
            long trieMemory,
            long invertedIndexMemory,
            long bloomFilterMemory)
        {
            var csvPath = Path.Combine(Directory.GetCurrentDirectory(), "MemoryUsageResults.csv");
            bool fileExists = File.Exists(csvPath);

            using var writer = new StreamWriter(csvPath, append: fileExists);

            if (!fileExists)
            {
                writer.WriteLine(
                    "File Size,Total Tokens,Unique Tokens," +
                    "Trie Memory (Bytes),Trie Memory (MB)," +
                    "Inverted Index Memory (Bytes),Inverted Index Memory (MB)," +
                    "Bloom Filter Memory (Bytes),Bloom Filter Memory (MB)," +
                    "Total Memory (Bytes),Total Memory (MB)"
                );
            }

            long totalMemory = trieMemory + invertedIndexMemory + bloomFilterMemory;
            double trieMB = trieMemory / (1024.0 * 1024.0);
            double invMB = invertedIndexMemory / (1024.0 * 1024.0);
            double bloomMB = bloomFilterMemory / (1024.0 * 1024.0);
            double totalMB = totalMemory / (1024.0 * 1024.0);

            writer.WriteLine(
                $"{fileSize},{totalTokens},{uniqueTokens}," +
                $"{trieMemory},{trieMB:F2}," +
                $"{invertedIndexMemory},{invMB:F2}," +
                $"{bloomFilterMemory},{bloomMB:F2}," +
                $"{totalMemory},{totalMB:F2}"
            );

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
}
