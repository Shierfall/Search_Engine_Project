using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Tokenizers;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;

namespace ManualBenchmarks
{
    public class ManualSearchBenchmark
    {
        private static readonly string[] FileSizes = { "100KB", "1MB", "2MB", "5MB", "10MB", "20MB", "50MB", "100MB", "200MB", "400MB", "800Mb"};
        private static readonly string BasePath = "/zhome/6b/1/188023/Downloads/texts/"; // Adjust as needed
        private static readonly int Iterations = 25; // Adjustable

        // define search methods
        private enum SearchMethod
        {
            Exact,
            Prefix,
            Phrase,
            Boolean,
            BooleanNaive
        }

        private static readonly Dictionary<SearchMethod, List<string>> QueryCategories = new()
        {
            [SearchMethod.Exact] = new() { "and", "or", "cat", "because" },
            [SearchMethod.Prefix] = new() { "an*", "or*", "ca*", "br*" },
            [SearchMethod.Phrase] = new() { "it was", "it was a", "and when", "or when" },
            [SearchMethod.Boolean] = new() { "and && or || cat", "cat && because", "because && cat || or", "or && and" },
            [SearchMethod.BooleanNaive] = new() { "and && or || cat", "cat && because", "because && cat || or", "or && and" }
        };

        /// <summary>
        /// parse a file with documents separated by ---end.of.document--- markers
        /// returns a list of (title, content) pairs representing real documents
        /// </summary>
        private static List<(string title, string content)> ParseDocuments(string filePath)
        {
            var documents = new List<(string title, string content)>();
            
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            string? line;
            string? currentTitle = null;
            var sb = new StringBuilder();
            bool titleRead = false;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.Trim() == "---END.OF.DOCUMENT---")
                {
                    if (titleRead && currentTitle != null && sb.Length > 0)
                    {
                        documents.Add((currentTitle, sb.ToString().Trim()));
                    }

                    titleRead = false;
                    currentTitle = null;
                    sb.Clear();
                    continue;
                }

                if (!titleRead)
                {
                    currentTitle = line;
                    titleRead = true;
                }
                else
                {
                    sb.AppendLine(line);
                }
            }

            // handle case where file doesn't end with the marker
            if (titleRead && currentTitle != null && sb.Length > 0)
            {
                documents.Add((currentTitle, sb.ToString().Trim()));
            }

            return documents;
        }

        public static void RunBenchmark()
        {
            var csvPath = "search_benchmark_results.csv";
            using var writer = new StreamWriter(csvPath, false, System.Text.Encoding.UTF8);
            writer.WriteLine("filesize,datastructure,searchmethod,query,time_ms");

            foreach (var fileSize in FileSizes)
            {
                var filePath = Path.Combine(BasePath, $"{fileSize}.txt");
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"File not found: {filePath}");
                    continue;
                }

                Console.WriteLine($"Parsing documents from file: {fileSize}");
                
                // parse real documents from the file
                var documents = ParseDocuments(filePath);
                if (documents.Count == 0)
                {
                    Console.WriteLine($"No documents found in {filePath}. File should contain documents separated by '---END.OF.DOCUMENT---' markers.");
                    continue;
                }

                Console.WriteLine($"Found {documents.Count} documents in {fileSize}");
                
                // create analyzer
                var analyzer = new Analyzer(new MinimalTokenizer());
                
                // tokenize all documents and prepare for indexing
                var documentTokens = new List<(int docId, List<Token> tokens)>();
                for (int i = 0; i < documents.Count; i++)
                {
                    var tokens = analyzer.Analyze(documents[i].content).ToList();
                    if (tokens.Count > 0)
                    {
                        documentTokens.Add((i, tokens));
                    }
                }

                Console.WriteLine($"Indexing {documentTokens.Count} documents for file size: {fileSize}");
                
                // index for trie using batch processing
                var trie = new CompactTrieIndex();
                trie.SetBM25Enabled(false);
                var trieTimer = Stopwatch.StartNew();
                
                // convert to format expected by adddocumentsbatch
                var trieDocTokens = documentTokens
                    .Select(dt => (dt.docId, (IEnumerable<Token>)dt.tokens))
                    .ToList();
                trie.AddDocumentsBatch(trieDocTokens);
                
                trieTimer.Stop();
                Console.WriteLine($"Trie indexing time: {trieTimer.ElapsedMilliseconds}ms");

                // index for invertedindex using batch processing
                var inverted = new InvertedIndex();
                inverted.SetBM25Enabled(false);
                var invertedTimer = Stopwatch.StartNew();
                
                // convert to format expected by adddocumentsbatch
                var invertedDocTokens = documentTokens
                    .Select(dt => (dt.docId, (IEnumerable<Token>)dt.tokens))
                    .ToList();
                inverted.AddDocumentsBatch(invertedDocTokens);
                
                invertedTimer.Stop();
                Console.WriteLine($"Inverted index indexing time: {invertedTimer.ElapsedMilliseconds}ms");

                // index for bloom filter
                var bloomFilter = new BloomFilter(1000000, 0.01); // assuming max 1M unique terms
                var bloomTimer = Stopwatch.StartNew();
                
                foreach (var (docId, tokens) in documentTokens)
                {
                    foreach (var token in tokens)
                    {
                        bloomFilter.Add(token.Term);
                    }
                }
                
                bloomTimer.Stop();
                Console.WriteLine($"Bloom filter indexing time: {bloomTimer.ElapsedMilliseconds}ms");

                foreach (var (searchMethod, queries) in QueryCategories)
                {
                    foreach (var query in queries)
                    {
                        // trie
                        double totalTimeTrie = 0;
                        for (int i = 0; i < Iterations; i++)
                        {
                            var sw = Stopwatch.StartNew();
                            switch (searchMethod)
                            {
                                case SearchMethod.Exact:
                                    trie.ExactSearch(query);
                                    break;
                                case SearchMethod.Prefix:
                                    trie.PrefixSearch(query.TrimEnd('*'));
                                    break;
                                case SearchMethod.Phrase:
                                    trie.PhraseSearch(query);
                                    break;
                                case SearchMethod.Boolean:
                                    trie.BooleanSearch(query);
                                    break;
                                case SearchMethod.BooleanNaive:
                                    trie.BooleanSearchNaive(query);
                                    break;
                            }
                            sw.Stop();
                            totalTimeTrie += sw.Elapsed.TotalMilliseconds;
                        }
                        double meanTrie = totalTimeTrie / Iterations;
                        writer.WriteLine($"{fileSize},CompactTrieIndex,{searchMethod},{query},{meanTrie.ToString(CultureInfo.InvariantCulture)}");

                        // inverted index
                        double totalTimeInv = 0;
                        for (int i = 0; i < Iterations; i++)
                        {
                            var sw = Stopwatch.StartNew();
                            switch (searchMethod)
                            {
                                case SearchMethod.Exact:
                                    inverted.ExactSearch(query);
                                    break;
                                case SearchMethod.Prefix:
                                    inverted.PrefixSearch(query.TrimEnd('*'));
                                    break;
                                case SearchMethod.Phrase:
                                    inverted.PhraseSearch(query);
                                    break;
                                case SearchMethod.Boolean:
                                    inverted.BooleanSearch(query);
                                    break;
                                case SearchMethod.BooleanNaive:
                                    inverted.BooleanSearchNaive(query);
                                    break;
                            }
                            sw.Stop();
                            totalTimeInv += sw.Elapsed.TotalMilliseconds;
                        }
                        double meanInv = totalTimeInv / Iterations;
                        writer.WriteLine($"{fileSize},InvertedIndex,{searchMethod},{query},{meanInv.ToString(CultureInfo.InvariantCulture)}");

                        // bloom filter (only for exact search since it only supports existence checks)
                        if (searchMethod == SearchMethod.Exact)
                        {
                            double totalTimeBloom = 0;
                            for (int i = 0; i < Iterations; i++)
                            {
                                var sw = Stopwatch.StartNew();
                                bloomFilter.MightContain(query);
                                sw.Stop();
                                totalTimeBloom += sw.Elapsed.TotalMilliseconds;
                            }
                            double meanBloom = totalTimeBloom / Iterations;
                            writer.WriteLine($"{fileSize},BloomFilter,{searchMethod},{query},{meanBloom.ToString(CultureInfo.InvariantCulture)}");
                        }
                    }
                }
                writer.Flush();
                Console.WriteLine($"Benchmarked file size: {fileSize}");
            }
            Console.WriteLine($"Benchmark complete. Results written to {csvPath}");
        }
    }
}
