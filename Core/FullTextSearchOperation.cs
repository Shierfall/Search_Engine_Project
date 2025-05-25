using System.Threading.Tasks;
using SearchEngine.Core.Interfaces;
using System.Collections.Generic;

namespace SearchEngine.Core;

public class FullTextSearchOperation : ISearchOperation
{
    public string Name => "fulltext";
    private readonly IFullTextIndex _index;

    public FullTextSearchOperation(IFullTextIndex index)
    {
        _index = index;
    }

    public Task<object> SearchAsync(string query)
    {
        // check if it's a boolean search (contains && or ||)
        if (query.Contains("&&") || query.Contains("||"))
        {
            return Task.FromResult<object>(_index.BooleanSearch(query));
        }

        // check if it's a phrase search (contains spaces)
        if (query.Contains(' '))
        {
            return Task.FromResult<object>(_index.PhraseSearch(query));
        }
        
        // default to exact search
        return Task.FromResult<object>(_index.ExactSearch(query));
    }
} 