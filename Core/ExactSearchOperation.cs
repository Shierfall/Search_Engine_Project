using System.Threading.Tasks;
using SearchEngine.Core.Interfaces;

namespace SearchEngine.Core;

public class ExactSearchOperation : ISearchOperation
{
    public string Name => "exact";
    private readonly IExactPrefixIndex _trie;
    public ExactSearchOperation(IExactPrefixIndex trie)
    {
        _trie = trie;
    }

    public Task<object> SearchAsync(string query)
    {
        // The query is already normalized by SearchService
        bool found = _trie.Search(query);
        return Task.FromResult<object>(found);
    }
}

