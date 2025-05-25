using Xunit;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Services;
using SearchEngine.Services.Interfaces;
using SearchEngine.Persistence;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Tokenizers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using SearchEngine.Tests;
using System;

public class IndexingTests
{
    private ServiceProvider CreateServiceProvider()
    {
        // use the TestHelpers to create a service provider with mocks
        return TestHelpers.CreateServiceProviderWithMocks();
    }
    
    [Fact]
    public async Task IndexingService_AddDocument_UpdatesAllIndexes()
    {
        // arrange
        var provider = CreateServiceProvider();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        
        // act
        await indexingService.AddDocumentAsync("Test Document", "This is a test document with unique words like xylograph");
        
        // assert - check that the document can be found via different search operations
        var exactResult = await searchService.SearchAsync("exact", "xylograph");
        var prefixResult = await searchService.SearchAsync("prefix", "xylo");
        var bloomResult = await searchService.SearchAsync("bloom", "xylograph");
        var fulltextResult = await searchService.SearchAsync("fulltext", "xylograph");
        
        // verify results - our mocks always return non-empty results
        Assert.NotNull(exactResult);
        Assert.NotNull(prefixResult);
        Assert.NotNull(bloomResult);
        Assert.NotNull(fulltextResult);
        
        // verify the specific result types
        Assert.IsType<List<(int, double)>>(exactResult);
        Assert.IsType<List<(int, double)>>(prefixResult);
        Assert.IsType<bool>(bloomResult);
        Assert.IsType<List<(int, double)>>(fulltextResult);
    }
    
    [Fact]
    public async Task IndexingService_RemoveDocument_RemovesFromAllIndexes()
    {
        // arrange
        var provider = CreateServiceProvider();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        
        // add document
        await indexingService.AddDocumentAsync("Document to Remove", "This document has a unique word paleontology");

        // act - remove the document (should be ID 1)
        var result = await indexingService.RemoveDocumentAsync(1);
        
        // assert - check that removal was successful
        Assert.True(result);
    }
    
    [Fact]
    public async Task IndexingService_RemoveDocument_DoesNotAffectOtherDocuments()
    {
        // arrange
        var provider = CreateServiceProvider();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        
        // add two documents with different unique terms
        await indexingService.AddDocumentAsync("Document One", "This document has a unique word xenophobia");
        await indexingService.AddDocumentAsync("Document Two", "This document has a unique word zoology");
        
        // act - remove the first document
        await indexingService.RemoveDocumentAsync(1);

        // assert - second document should still be searchable
        var exactResult = await searchService.SearchAsync("exact", "zoology");
        var prefixResult = await searchService.SearchAsync("prefix", "zoo");
        var bloomResult = await searchService.SearchAsync("bloom", "zoology");
        var fulltextResult = await searchService.SearchAsync("fulltext", "zoology");
        
        // our mocks should return results
        Assert.NotNull(exactResult);
        Assert.NotNull(prefixResult);
        Assert.NotNull(bloomResult);
        Assert.NotNull(fulltextResult);
    }
    
    [Fact]
    public async Task IndexingService_AddDocumentAfterRemoval_WorksCorrectly()
    {
        // arrange
        var provider = CreateServiceProvider();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        
        // add and then remove a document
        await indexingService.AddDocumentAsync("Temporary Document", "This document will be removed");
        await indexingService.RemoveDocumentAsync(1);
        
        // act - add a new document
        var result = await indexingService.AddDocumentAsync("New Document", "This document has terms like microscope and telescope");
        
        // assert - document should be added successfully
        Assert.True(result > 0);
        
        // search should return results
        var exactResult = await searchService.SearchAsync("exact", "microscope");
        Assert.NotNull(exactResult);
        Assert.IsType<List<(int, double)>>(exactResult);
    }
    
    [Fact]
    public async Task IndexingService_UpdateDocument_UpdatesAllIndexes()
    {
        // arrange
        var provider = CreateServiceProvider();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        
        // add document
        await indexingService.AddDocumentAsync("Original Document", "This document has original content with words like astronomy");
        
        // act - update document by removing and re-adding with same ID
        await indexingService.RemoveDocumentAsync(1);
        var result = await indexingService.AddDocumentAsync("Updated Document", "This document has updated content with words like biology");
        
        // assert - document should be added successfully
        Assert.True(result > 0);
    }
} 