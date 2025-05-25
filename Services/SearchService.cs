using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SearchEngine.Analysis;
using SearchEngine.Core.Interfaces;
using SearchEngine.Persistence;
using SearchEngine.Persistence.Entities;
using SearchEngine.Services.Interfaces;
namespace SearchEngine.Services;

public class SearchService : ISearchService
{
    private readonly IDictionary<string, ISearchOperation> _ops;
    private readonly IDocumentService _docs;
    private readonly Analyzer _analyzer;
    private readonly IFullTextIndex _invertedIndex;

    public SearchService(IEnumerable<ISearchOperation> ops, IDocumentService docs, Analyzer analyzer, IFullTextIndex invertedIndex)
    {
        _ops = ops.ToDictionary(
                op => op.Name,
                op => op,
                StringComparer.OrdinalIgnoreCase);
        _docs = docs;
        _analyzer = analyzer;
        _invertedIndex = invertedIndex;
    }

    public async Task<object> SearchAsync(string operation, string query)
    {
        if (!_ops.TryGetValue(operation, out var op))
            throw new InvalidOperationException($"Unknown search operation '{operation}'");

        string normalizedQuery = NormalizeQuery(query);
        
        if (operation.Equals("autocomplete", StringComparison.OrdinalIgnoreCase))
        {
            normalizedQuery = query;
        }

        var raw = await op.SearchAsync(normalizedQuery);

        // handle count-based results (from any search type)
        if (raw is List<(int docId, int count)> countResults)
        {
            // just return the raw count results without normalization
            return countResults;
        }
        
        if (raw is List<int> ids)
        {
            var titles = new List<string>(ids.Count);
            foreach (var id in ids)
                titles.Add(await _docs.GetTitleAsync(id));
            return titles;
        }

        if (raw is List<(string word, List<int> ids)> ac)
        {
            var output = new List<(string word, List<string> titles)>(ac.Count);
            foreach (var (word, ids2) in ac)
            {
                var titleList = new List<string>(ids2.Count);
                foreach (var id in ids2)
                    titleList.Add(await _docs.GetTitleAsync(id));
                output.Add((word, titleList));
            }
            return output;
        }

        return raw;
    }

    private string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query;
            
        bool isPrefix = query.EndsWith("*");
        bool containsOperators = query.Contains("&&") || query.Contains("||");
        
        string cleanQuery = query;
        
        // remove special characters before analysis
        if (isPrefix)
            cleanQuery = query.Substring(0, query.Length - 1);
        else if (query.EndsWith("#"))
            cleanQuery = query.Substring(0, query.Length - 1);
            
        // if the query has boolean operators, we need to process each term separately
        if (containsOperators)
        {
            var parts = new List<string>();
            var terms = cleanQuery.Split(new[] { "&&", "||" }, StringSplitOptions.RemoveEmptyEntries);
            
            string[] operators = ExtractOperators(query);
            
            for (int i = 0; i < terms.Length; i++)
            {
                var normalized = NormalizeQueryTerm(terms[i].Trim());
                parts.Add(normalized);
                
                if (i < operators.Length)
                    parts.Add(operators[i]);
            }
            
            return string.Join(" ", parts);
        }
        
        // for phrase queries, normalize each word while preserving spaces
        if (cleanQuery.Contains(" "))
        {
            var words = cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var normalizedWords = words.Select(NormalizeQueryTerm);
            cleanQuery = string.Join(" ", normalizedWords);
        }
        else
        {
            // for simple queries, just normalize the whole query
            cleanQuery = NormalizeQueryTerm(cleanQuery);
        }
        
        if (isPrefix)
            cleanQuery += "*";
            
        return cleanQuery;
    }
    
    private string NormalizeQueryTerm(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return term;
            
        var tokens = _analyzer.Analyze(term).ToList();
        
        if (tokens.Count == 0)
            return term.ToLowerInvariant();
            
        return tokens[0].Term;
    }
    
    private string[] ExtractOperators(string query)
    {
        var operators = new List<string>();
        int i = 0;
        
        while (i < query.Length - 1)
        {
            if (i + 2 <= query.Length && query.Substring(i, 2) == "&&")
            {
                operators.Add("&&");
                i += 2;
            }
            else if (i + 2 <= query.Length && query.Substring(i, 2) == "||")
            {
                operators.Add("||");
                i += 2;
            }
            else
            {
                i++;
            }
        }
        
        return operators.ToArray();
    }

    public async Task<string> GetTitleAsync(int documentId)
    {
        return await _docs.GetTitleAsync(documentId);
    }
    
    public Task SetBM25ParamsAsync(double k1, double b)
    {
        if (_invertedIndex is InvertedIndex bm25Index)
        {
            bm25Index.SetBM25Params(k1, b);
        }
        return Task.CompletedTask;
    }
    
    public Task<(double k1, double b)> GetBM25ParamsAsync()
    {
        if (_invertedIndex is InvertedIndex bm25Index)
        {
            return Task.FromResult(bm25Index.GetBM25Params());
        }
        return Task.FromResult((1.2, 0.75)); // default values if not an InvertedIndex
    }
}
