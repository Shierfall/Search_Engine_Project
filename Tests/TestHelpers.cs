using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Tokenizers;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Persistence;
using SearchEngine.Services;
using SearchEngine.Services.Interfaces;
using SearchEngine.Tests.Mocks;
using System;

namespace SearchEngine.Tests
{
    public static class TestHelpers
    {
        public static ServiceProvider CreateServiceProviderWithMocks()
        {
            var services = new ServiceCollection();
            
            // database context with in-memory database
            services.AddDbContext<SearchEngineContext>(options => 
                options.UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}"));
                
            // repository services
            services.AddScoped<DocumentRepository>();
            services.AddScoped<DocumentTermRepository, MockDocumentTermRepository>(); // Use our mock implementation
            services.AddScoped<IDocumentService, DocumentService>();
            
            // analysis pipeline
            services.AddSingleton<Analyzer>(sp => new Analyzer(new MinimalTokenizer()));
            
            // indexes
            services.AddSingleton<IExactPrefixIndex, CompactTrieIndex>();
            services.AddSingleton<IFullTextIndex, InvertedIndex>();
            services.AddSingleton<IBloomFilter>(provider => new BloomFilter(1000, 0.01));
            
            // search operations - use our mock implementations
            services.AddSingleton<ISearchOperation, MockExactSearchOperation>();
            services.AddSingleton<ISearchOperation, MockPrefixDocsSearchOperation>();
            services.AddSingleton<ISearchOperation, MockAutoCompleteSearchOperation>();
            services.AddSingleton<ISearchOperation, MockFullTextSearchOperation>();
            services.AddSingleton<ISearchOperation, MockBloomFilterSearchOperation>();
            
            // search and indexing services
            services.AddScoped<IIndexingService, IndexingService>();
            services.AddScoped<ISearchService, SearchService>();
            
            return services.BuildServiceProvider();
        }
    }
} 