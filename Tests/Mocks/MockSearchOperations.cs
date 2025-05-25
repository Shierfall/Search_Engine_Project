using System.Threading.Tasks;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace SearchEngine.Tests.Mocks
{
    public class MockExactSearchOperation : ISearchOperation
    {
        public string Name => "exact";
        
        public Task<object> SearchAsync(string query)
        {
            // return a list of (int, double) tuples for document IDs and scores
            var result = new List<(int, double)> { (1, 1.0) };
            return Task.FromResult<object>(result);
        }
    }
    
    public class MockFullTextSearchOperation : ISearchOperation
    {
        public string Name => "fulltext";
        
        public Task<object> SearchAsync(string query)
        {
            // return a list of (int, double) tuples for document IDs and scores
            var result = new List<(int, double)> 
            { 
                (1, 0.8), 
                (2, 0.6) 
            };
            return Task.FromResult<object>(result);
        }
    }
    
    public class MockAutoCompleteSearchOperation : ISearchOperation
    {
        public string Name => "autocomplete";
        
        public Task<object> SearchAsync(string query)
        {
            // return a list of (string, List<string>) tuples for terms and document titles
            var result = new List<(string, List<string>)>
            {
                ("test", new List<string> { "Test Document" }),
                ("testing", new List<string> { "Testing Document" })
            };
            return Task.FromResult<object>(result);
        }
    }
    
    public class MockBloomFilterSearchOperation : ISearchOperation
    {
        public string Name => "bloom";
        
        public Task<object> SearchAsync(string query)
        {
            // return a boolean indicating if the term is in the bloom filter
            return Task.FromResult<object>(true);
        }
    }
} 