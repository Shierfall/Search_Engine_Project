using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    public IndexingService(
        Analyzer analyzer, 
        IDocumentService docs, 
        DocumentTermRepository terms, 
        IEnumerable<IExactPrefixIndex> prefixIndexes,
        IFullTextIndex fullTextIndex,
        IBloomFilter bloomFilter)
    {
        _analyzer = analyzer;
        _docs = docs;
        _terms = terms;
        _prefixIndexes = prefixIndexes;
        _fullTextIndex = fullTextIndex;
        _bloomFilter = bloomFilter;
    }

    public async Task<int> AddDocumentAsync(string title, string content)
    {
        //persist the metadata
        int docId = await _docs.CreateAsync(title);

        // anlyse then persist the tokens then index it into our structure
        var tokens = _analyzer.Analyze(content)
                              .ToList();
        await _terms.BulkUpsertTermsAsync(docId, tokens);

        // Index into prefix indexes (trie)
        foreach (var index in _prefixIndexes)
        {
            index.AddDocument(docId, tokens);
        }

        // Index into full-text index
        _fullTextIndex.AddDocument(docId, tokens);

        // Add terms to Bloom filter
        foreach (var token in tokens)
        {
            _bloomFilter.Add(token.Term);
        }

        return docId;
    }

    public async Task<bool> RemoveDocumentAsync(int docId)
    {
        // Remove from database
        bool existed = await _docs.DeleteAsync(docId);
        if (!existed) return false;

        // Remove from prefix indexes
        var tokens = await _docs.GetIndexedTokensAsync(docId);
        foreach (var index in _prefixIndexes)
        {
            index.RemoveDocument(docId, tokens);
        }

        // Remove from full-text index
        _fullTextIndex.RemoveDocument(docId, tokens);

        // Note: We don't remove terms from the Bloom filter because
        // Bloom filters don't support deletion. This is fine because:
        // 1. The Bloom filter only gives false positives, never false negatives
        // 2. The actual search will still return correct results
        // 3. The false positive rate will increase slightly over time
        // If this becomes a problem, we can rebuild the Bloom filter periodically

        return true;
    }

    public async Task RebuildIndexAsync()
    {
        // Clear all indexes
        foreach (var index in _prefixIndexes)
        {
            index.Clear();
        }
        _fullTextIndex.Clear();
        // Note: We don't clear the Bloom filter because it's a probabilistic data structure
        // that doesn't support deletion. Instead, we'll rebuild it from scratch.

        // Rebuild from database
        var docs = await _docs.GetAllAsync();
        foreach (var doc in docs)
        {
            var tokens = await _docs.GetIndexedTokensAsync(doc.Id);
            foreach (var index in _prefixIndexes)
            {
                index.AddDocument(doc.Id, tokens);
            }
            _fullTextIndex.AddDocument(doc.Id, tokens);

            // Add terms to Bloom filter
            foreach (var token in tokens)
            {
                _bloomFilter.Add(token.Term);
            }
        }
    }

    public async Task IndexDocumentByIdAsync(int docId, string content)
    {
        var tokens = _analyzer.Analyze(content).ToList();
        await _terms.BulkUpsertTermsAsync(docId, tokens);

        foreach (var index in _prefixIndexes)
            index.AddDocument(docId, tokens);

        _fullTextIndex.AddDocument(docId, tokens);

        foreach (var token in tokens)
            _bloomFilter.Add(token.Term);
    }
}
