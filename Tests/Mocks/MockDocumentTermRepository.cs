using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using SearchEngine.Analysis;
using SearchEngine.Persistence;
using SearchEngine.Persistence.Entities;

namespace SearchEngine.Tests.Mocks
{
    public class MockDocumentTermRepository : DocumentTermRepository
    {
        private readonly SearchEngineContext _context;

        public MockDocumentTermRepository(SearchEngineContext context) : base(context)
        {
            _context = context;
        }

        /// <summary>
        /// Override the BulkUpsertTermsAsync method to use standard EF Core operations
        /// instead of BulkExtensions which doesn't work with InMemory provider
        /// </summary>
        public override async Task BulkUpsertTermsAsync(int docId, IEnumerable<Token> tokens)
        {
            // use the UpsertManyAsync implementation which doesn't use BulkExtensions
            await UpsertManyAsync(docId, tokens);
        }
    }
} 