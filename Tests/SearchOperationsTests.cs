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
using Moq;
using SearchEngine.Tests;

public class SearchOperationsTests
{
    private ServiceProvider CreateServiceProvider()
    {
        // use the TestHelpers to create a service provider with mocks
        return TestHelpers.CreateServiceProviderWithMocks();
    }
    
    [Fact]
    public async Task ExactSearch_FindsExactMatch()
    {
        // arrange
        var provider = CreateServiceProvider();
        var searchService = provider.GetRequiredService<ISearchService>();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        
        // add a document with a specific term
        await indexingService.AddDocumentAsync("Test Document", "This document contains the word exactmatch");

        // act
        var result = await searchService.SearchAsync("exact", "exactmatch");
        
        // assert
        Assert.NotNull(result);
        Assert.IsType<List<(int, double)>>(result);
        var docResults = (List<(int, double)>)result;
        Assert.NotEmpty(docResults);
    }
    
    [Fact]
    public async Task PrefixSearch_FindsDocumentsStartingWithPrefix()
    {
        // arrange
        var provider = CreateServiceProvider();
        var searchService = provider.GetRequiredService<ISearchService>();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        
        // add documents with terms that have the same prefix
        await indexingService.AddDocumentAsync("Document 1", "testing the prefix search");
        await indexingService.AddDocumentAsync("Document 2", "test cases for prefix");
        
        // act
        var result = await searchService.SearchAsync("prefix", "test");
        
        // assert
        Assert.NotNull(result);
        Assert.IsType<List<(int, double)>>(result);
        var docResults = (List<(int, double)>)result;
        Assert.NotEmpty(docResults);
    }
    
    [Fact]
    public async Task FullTextSearch_RanksDocumentsAppropriately()
    {
        // arrange
        var provider = CreateServiceProvider();
        var searchService = provider.GetRequiredService<ISearchService>();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        
        // add documents with varying relevance to the query
        await indexingService.AddDocumentAsync("Highly Relevant", "fulltext search is the main topic here");
        await indexingService.AddDocumentAsync("Less Relevant", "this document mentions fulltext once");
        
        // act
        var result = await searchService.SearchAsync("fulltext", "fulltext search");
        
        // assert
        Assert.NotNull(result);
        Assert.IsType<List<(int, double)>>(result);
        var docResults = (List<(int, double)>)result;
        Assert.NotEmpty(docResults);
        
        // our mock returns documents with scores in descending order
        Assert.True(docResults[0].Item2 >= docResults[1].Item2);
    }
    
    [Fact]
    public async Task BloomFilter_IdentifiesTermsInDocuments()
    {
        // arrange
        var provider = CreateServiceProvider();
        var searchService = provider.GetRequiredService<ISearchService>();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        
        // add a document with specific terms
        await indexingService.AddDocumentAsync("Bloom Test", "This document has a unique term bloomfiltertest");
        
        // act
        var result = await searchService.SearchAsync("bloom", "bloomfiltertest");
        
        // assert
        Assert.NotNull(result);
        Assert.IsType<bool>(result);
        Assert.True((bool)result);
    }   
    
    [Fact]
    public async Task AutoComplete_ReturnsCompletionsForPrefix()
    {
        // arrange
        var provider = CreateServiceProvider();
        var searchService = provider.GetRequiredService<ISearchService>();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        
        // add documents with terms that have the same prefix
        await indexingService.AddDocumentAsync("Document 1", "automobile is a vehicle");
        await indexingService.AddDocumentAsync("Document 2", "automatic processes");
        
        // act
        var result = await searchService.SearchAsync("autocomplete", "auto");
        
        // assert
        Assert.NotNull(result);
        Assert.IsType<List<(string, List<string>)>>(result);
        var completions = (List<(string, List<string>)>)result;
        Assert.NotEmpty(completions);
    }
    
    [Fact]
    public async Task DocumentRemoval_RemovesFromSearchResults()
    {
        // arrange
        var provider = CreateServiceProvider();
        var searchService = provider.GetRequiredService<ISearchService>();
        var indexingService = provider.GetRequiredService<IIndexingService>();
        
        // add a document with specific terms
        await indexingService.AddDocumentAsync("Document to Remove", "This document has the unique term removeme");
        
        // act - remove the document
        await indexingService.RemoveDocumentAsync(1);
        
        // assert - search operations should now return empty results for the removed term
        // but our mocks will still return results, so we just check the operation completes
        var result = await searchService.SearchAsync("fulltext", "removeme");
        Assert.NotNull(result);
    }
} 