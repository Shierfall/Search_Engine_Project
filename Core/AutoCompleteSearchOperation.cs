using System.Threading.Tasks;
using SearchEngine.Core.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System;
namespace SearchEngine.Core;

public class AutoCompleteSearchOperation : ISearchOperation
{
    public string Name => "autocomplete";
    private readonly IExactPrefixIndex _trie;
    
    public AutoCompleteSearchOperation(IExactPrefixIndex trie)
    {
        _trie = trie;
    }

    public Task<object> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<object>(new List<string>());
        }
        
        // Get prefix matches for the query (query is already normalized by SearchService if needed)
        List<(string word, List<int> docIds)> hits = _trie.PrefixSearch(query);
        
        // if no results, try with progressively shorter prefixes until we find matches
        // or until we reach a minimum prefix length (2 characters)
        string currentQuery = query;
        int minPrefixLength = 2; // dont go shorter than 2 characters
        
        while (hits.Count == 0 && currentQuery.Length > minPrefixLength)
        {
            // try with one character less
            currentQuery = currentQuery.Substring(0, currentQuery.Length - 1);
            hits = _trie.PrefixSearch(currentQuery);
            
            // if we find hits with the shorter prefix, filter them to only include words that would be relevant to the original prefix
            if (hits.Count > 0)
            {
                hits = hits
                    .Where(h => {
                        // always accept if it starts with our current prefix
                        if (h.Item1.StartsWith(currentQuery)) {
                            if (query.Length > currentQuery.Length) {
                                // calculate how much of the original query is contained in the beginning of the word
                                int matchLength = 0;
                                for (int i = 0; i < Math.Min(query.Length, h.Item1.Length); i++) {
                                    if (query[i] == h.Item1[i]) {
                                        matchLength++;
                                    } else {
                                        break;
                                    }
                                }
                                
                                // accept if at least 60% of the original query matches
                                // the beginning of the word
                                double matchPercentage = (double)matchLength / query.Length;
                                return matchPercentage >= 0.6;
                            }
                            return true;
                        }
                        return false;
                    })
                    .ToList();
            }
        }
        
        //  first group by whether they start with the original query
        var groupedResults = hits
            .GroupBy(h => h.Item1.StartsWith(query))
            .OrderByDescending(g => g.Key) // true group first (exact prefix matches)
            .SelectMany(g => g
                .OrderByDescending(h => h.Item2.Count) // by popularity
                .ThenBy(h => h.Item1.Length)          // then by length
                .Select(h => h.Item1)                 // just the word
            )
            .Distinct()
            .ToList();
        
        // return just the list of words
        return Task.FromResult<object>(groupedResults);
    }
}
