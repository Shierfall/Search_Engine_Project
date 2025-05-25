using System.Threading.Tasks;
using SearchEngine.Core.Interfaces;

namespace SearchEngine.Core;

public class BloomFilterSearchOperation : ISearchOperation
{
    public string Name => "bloom";
    private readonly IBloomFilter _bloomFilter;

    public BloomFilterSearchOperation(IBloomFilter bloomFilter)
    {
        _bloomFilter = bloomFilter;
    }

    public Task<object> SearchAsync(string query)
    {
        // The query is already normalized by SearchService
        return Task.FromResult<object>(_bloomFilter.MightContain(query));
    }
} 