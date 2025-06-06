using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SearchEngine.Analysis;
using SearchEngine.Core.Interfaces;
using SearchEngine.Persistence;
using SearchEngine.Persistence.Entities;
using SearchEngine.Services.Interfaces;

namespace SearchEngine.Services;
/// <summary>
/// Responsible for tokenization, persistence (via bulk ops), and in-memory indexing.
/// </summary>
public class IndexingService : IIndexingService
{
    private readonly Analyzer _analyzer;
    private readonly IDocumentService _docs;
    private readonly DocumentTermRepository _terms;
    private readonly IEnumerable<IExactPrefixIndex> _prefixIndexes;
    private readonly IFullTextIndex _fullTextIndex;
    private readonly IBloomFilter _bloomFilter;
    private readonly int _maxParallelism;
    private readonly ILogger<IndexingService> _logger;

    // Performance tuning parameters
    private const int BATCH_SIZE = 1000; // Larger batch size for better throughput
    private const int DB_COMMIT_SIZE = 10000; // How many records to commit to DB at once
    private const int GC_FREQUENCY = 5; // How often to run GC (every N batches)

    public IndexingService(
        Analyzer analyzer, 
        IDocumentService docs, 
        DocumentTermRepository terms, 
        IEnumerable<IExactPrefixIndex> prefixIndexes,
        IFullTextIndex fullTextIndex,
        IBloomFilter bloomFilter,
        ILogger<IndexingService> logger)
    {
        _analyzer = analyzer;
        _docs = docs;
        _terms = terms;
        _prefixIndexes = prefixIndexes;
        _fullTextIndex = fullTextIndex;
        _bloomFilter = bloomFilter;
        _maxParallelism = Math.Max(1, Environment.ProcessorCount);
        _logger = logger;
    }

    public async Task<int> AddDocumentAsync(string title, string content)
    {
        //persist the metadata
        int docId = await _docs.CreateAsync(title);

        // analyze and index in a separate task to not block
        await Task.Run(async () => {
            var tokens = _analyzer.Analyze(content).ToList();
            await _terms.BulkUpsertTermsAsync(docId, tokens);

            // index into prefix indexes (trie)
            foreach (var index in _prefixIndexes)
            {
                index.AddDocument(docId, tokens);
            }

            // index into full-text index
            _fullTextIndex.AddDocument(docId, tokens);

            // add terms to Bloom filter
            var uniqueTerms = tokens.Select(t => t.Term).Distinct().ToList();
            _bloomFilter.AddBatch(uniqueTerms);
        });

        return docId;
    }

    public async Task<bool> RemoveDocumentAsync(int docId)
    {
        // first, get the tokens for the document. We need them to clean the indexes.
        var tokens = await _docs.GetIndexedTokensAsync(docId);
        if (tokens == null || !tokens.Any())
        {
            // if there are no tokens, the document might not exist or was never indexed.
            // try to delete it from the DB just in case.
            // delete terms just in case they are orphaned.
            await _docs.DeleteTermsAsync(docId);
            return await _docs.DeleteAsync(docId);
        }

        // remove from prefix indexes
        foreach (var index in _prefixIndexes)
        {
            index.RemoveDocument(docId, tokens);
        }

        // remove from full-text index
        _fullTextIndex.RemoveDocument(docId, tokens);

        // the bloom filter is probabilistic and doesn't support removal, which is fine.

        // now that the in-memory indexes are clean, remove from the database.
        await _docs.DeleteTermsAsync(docId); // remove the terms
        bool existed = await _docs.DeleteAsync(docId); // then remove the document metadata

        return existed;
    }

    public async Task RebuildIndexAsync()
    {
        var totalTimer = Stopwatch.StartNew();
        
        // clear all indexes
        foreach (var index in _prefixIndexes)
        {
            index.Clear();
        }
        _fullTextIndex.Clear();
        
        // rebuild from database
        var docs = await _docs.GetAllAsync();
        if (docs.Count == 0) return;
        
        _logger.LogInformation("Rebuilding index for {DocumentCount} documents", docs.Count);
        
        // pre-allocate for better memory usage
        var allUniqueTerms = new ConcurrentDictionary<string, byte>();
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism };
        
        // process in large batches
        for (int i = 0; i < docs.Count; i += BATCH_SIZE)
        {
            var batchTimer = Stopwatch.StartNew();
            var batchDocs = docs.Skip(i).Take(BATCH_SIZE).ToList();
            
            int batchNumber = i/BATCH_SIZE + 1;
            int totalBatches = (docs.Count + BATCH_SIZE - 1)/BATCH_SIZE;
            
            _logger.LogInformation("Processing batch {BatchNumber} of {TotalBatches} ({BatchSize} documents)", 
                batchNumber, totalBatches, batchDocs.Count);
            
            // process all docs in the batch in parallel with minimal contention
            var docTokensBatch = new ConcurrentBag<(int docId, List<Token> tokens)>();
            
            // fetch tokens for all documents in batch - most time consuming part
            var fetchTimer = Stopwatch.StartNew();
            await Task.WhenAll(batchDocs.Select(async doc => {
                var tokens = await _docs.GetIndexedTokensAsync(doc.Id);
                docTokensBatch.Add((doc.Id, tokens.ToList()));
                
                // collect unique terms for bloom filter
                foreach (var term in tokens.Select(t => t.Term).Distinct())
                {
                    allUniqueTerms.TryAdd(term, 0);
                }
            }));
            fetchTimer.Stop();
            _logger.LogDebug("Batch {BatchNumber}: Token fetch time: {Time}ms", batchNumber, fetchTimer.ElapsedMilliseconds);
            
            // prepare data for indexing
            var processedBatch = docTokensBatch.Select(r => (r.docId, (IEnumerable<Token>)r.tokens)).ToList();
            
            // ndex in parallel across prefix indexes - these can be done independently
            var indexTimer = Stopwatch.StartNew();
            await Task.WhenAll(_prefixIndexes.Select(index => 
                Task.Run(() => index.AddDocumentsBatch(processedBatch))
            ));
            
            // update full-text index
            await Task.Run(() => _fullTextIndex.AddDocumentsBatch(processedBatch));
            indexTimer.Stop();
            _logger.LogDebug("Batch {BatchNumber}: Index update time: {Time}ms", batchNumber, indexTimer.ElapsedMilliseconds);
            
            // run GC occasionally to prevent memory pressure
            if (batchNumber % GC_FREQUENCY == 0)
            {
                var gcTimer = Stopwatch.StartNew();
                GC.Collect(2, GCCollectionMode.Optimized, true);
                gcTimer.Stop();
                _logger.LogDebug("Batch {BatchNumber}: GC time: {Time}ms", batchNumber, gcTimer.ElapsedMilliseconds);
            }
            
            batchTimer.Stop();
            _logger.LogInformation("Batch {BatchNumber} completed in {Time}ms", batchNumber, batchTimer.ElapsedMilliseconds);
        }
        
        // update bloom filter with all terms (single operation is more efficient)
        var bloomTimer = Stopwatch.StartNew();
        _logger.LogInformation("Adding {TermCount} unique terms to bloom filter", allUniqueTerms.Count);
        _bloomFilter.AddBatch(allUniqueTerms.Keys);
        bloomTimer.Stop();
        _logger.LogDebug("Bloom filter update time: {Time}ms", bloomTimer.ElapsedMilliseconds);
        
        totalTimer.Stop();
        _logger.LogInformation("Total rebuild time: {Time}ms", totalTimer.ElapsedMilliseconds);
    }
    
    public async Task IndexDocumentByIdAsync(int docId, string content)
    {
        var tokens = _analyzer.Analyze(content).ToList();
        await _terms.BulkUpsertTermsAsync(docId, tokens);

        foreach (var index in _prefixIndexes)
            index.AddDocument(docId, tokens);

        _fullTextIndex.AddDocument(docId, tokens);

        var uniqueTerms = tokens.Select(t => t.Term).Distinct().ToList();
        _bloomFilter.AddBatch(uniqueTerms);
    }
    
    public async Task IndexDocumentsBatchAsync(List<(int docId, string content)> documents)
    {
        if (documents.Count == 0) return;
        
        var batchTimer = Stopwatch.StartNew();
        _logger.LogInformation("Processing batch of {DocumentCount} documents", documents.Count);
        
        //  tokenize all documents in parallel and collect results
        var tokenizeTimer = Stopwatch.StartNew();
        var localResults = new ConcurrentBag<(int docId, List<Token> tokens, HashSet<string> terms)>();
        
        // set higher degree of parallelism for CPU-bound tokenization
        var parallelOptions = new ParallelOptions { 
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount * 2) 
        };
        
        Parallel.ForEach(documents, parallelOptions, doc =>
        {
            var tokens = _analyzer.Analyze(doc.content).ToList();
            var uniqueTerms = tokens.Select(t => t.Term).Distinct().ToHashSet();
            localResults.Add((doc.docId, tokens, uniqueTerms));
        });
        
        tokenizeTimer.Stop();
        _logger.LogDebug("Tokenization time: {Time}ms", tokenizeTimer.ElapsedMilliseconds);
        
        // prepare data for DB and indexes
        var dbTimer = Stopwatch.StartNew();
        var docTokensBatch = localResults.Select(r => (r.docId, (IEnumerable<Token>)r.tokens)).ToList();
        var allTerms = new HashSet<string>();
        
        //  batch DB operations for maximum efficiency
        var dbBatches = new List<List<(int docId, List<Token> tokens)>>();
        var currentBatch = new List<(int docId, List<Token> tokens)>();
        
        foreach (var result in localResults)
        {
            currentBatch.Add((result.docId, result.tokens));
            allTerms.UnionWith(result.terms);
            
            // ceate new batch when size limit reached
            if (currentBatch.Count >= DB_COMMIT_SIZE)
            {
                dbBatches.Add(currentBatch);
                currentBatch = new List<(int docId, List<Token> tokens)>();
            }
        }
        
        // add final batch if not empty
        if (currentBatch.Count > 0)
        {
            dbBatches.Add(currentBatch);
        }
        
        // process each DB batch sequentially (better for DB performance)
        foreach (var batch in dbBatches)
        {
            // process documents in this batch in parallel
            await Task.WhenAll(batch.Select(item => 
                _terms.BulkUpsertTermsAsync(item.docId, item.tokens)
            ));
        }
        
        dbTimer.Stop();
        _logger.LogDebug("DB operation time: {Time}ms", dbTimer.ElapsedMilliseconds);
        
        // update indexes in parallel - for maximum throughput
        var indexTimer = Stopwatch.StartNew();
        
        // run each index update as a separate task
        var indexTasks = _prefixIndexes.Select(index => 
            Task.Run(() => index.AddDocumentsBatch(docTokensBatch))
        ).ToList();
        
        // add full-text index task
        indexTasks.Add(Task.Run(() => _fullTextIndex.AddDocumentsBatch(docTokensBatch)));
        
        // add bloom filter task
        indexTasks.Add(Task.Run(() => _bloomFilter.AddBatch(allTerms)));
        
        // wait for all index updates to complete
        await Task.WhenAll(indexTasks);
        
        indexTimer.Stop();
        _logger.LogDebug("Index update time: {Time}ms", indexTimer.ElapsedMilliseconds);
        
        batchTimer.Stop();
        _logger.LogInformation("Batch processing completed in {Time}ms", batchTimer.ElapsedMilliseconds);
    }
}
