using System.Threading.Tasks;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace SearchEngine.Tests.Mocks
{
    public class MockPrefixDocsSearchOperation : ISearchOperation
    {
        private readonly IExactPrefixIndex _trie;
        
        public string Name => "prefix";
        
        public MockPrefixDocsSearchOperation(IExactPrefixIndex trie)
        {
            _trie = trie;
        }
        
        public Task<object> SearchAsync(string query)
        {
            // get document IDs from the trie
            List<int> ids = _trie.PrefixSearchDocuments(query);
            
            // convert to List<(int, double)> to match the expected return type in tests
            var result = ids.Select(id => (id, 1.0)).ToList();
            
            return Task.FromResult<object>(result);
        }
    }
} 