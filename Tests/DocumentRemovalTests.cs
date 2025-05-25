using Xunit;
using SearchEngine.Persistence;
using SearchEngine.Persistence.Entities;
using SearchEngine.Services;
using SearchEngine.Services.Interfaces;
using SearchEngine.Analysis;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Moq;
using SearchEngine.Tests.Mocks;
using System;

public class DocumentRemovalTests
{
    private SearchEngineContext CreateInMemoryContext(string dbName = null)
    {
        var options = new DbContextOptionsBuilder<SearchEngineContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? $"TestDatabase_{Guid.NewGuid()}")
            .Options;

        return new SearchEngineContext(options);
    }

    private IDocumentService CreateDocumentService(SearchEngineContext context)
    {
        var docRepo = new DocumentRepository(context);
        var termRepo = new MockDocumentTermRepository(context);
        return new DocumentService(docRepo, termRepo);
    }

    [Fact]
    public async Task DocumentService_RemovesDocumentSuccessfully()
    {
        // arrange
        var mockDocRepo = new Mock<DocumentRepository>(new MockSearchEngineContext());
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        mockDocRepo.Setup(repo => repo.DeleteAsync(It.IsAny<int>())).ReturnsAsync(true);
        
        var documentService = new DocumentService(mockDocRepo.Object, mockTermRepo.Object);

        // act
        var result = await documentService.DeleteAsync(1);

        // assert
        Assert.True(result);
        mockDocRepo.Verify(repo => repo.DeleteAsync(1), Times.Once);
    }
    
    [Fact]
    public async Task DocumentService_ReturnsFalse_WhenRemovingNonExistentDocument()
    {
        // arrange
        var mockDocRepo = new Mock<DocumentRepository>(new MockSearchEngineContext());
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        mockDocRepo.Setup(repo => repo.DeleteAsync(It.IsAny<int>())).ReturnsAsync(false);
        
        var documentService = new DocumentService(mockDocRepo.Object, mockTermRepo.Object);

        // act
        var result = await documentService.DeleteAsync(999);

        // assert
        Assert.False(result);
        mockDocRepo.Verify(repo => repo.DeleteAsync(999), Times.Once);
    }
    
    [Fact]
    public async Task DocumentService_DeleteTerms_RemovesAllTermsForDocument()
    {
        // arrange
        var mockDocRepo = new Mock<DocumentRepository>(new MockSearchEngineContext());
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        mockTermRepo.Setup(repo => repo.DeleteByDocumentAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        
        var documentService = new DocumentService(mockDocRepo.Object, mockTermRepo.Object);
        
        // act
        await documentService.DeleteTermsAsync(1);

        // assert
        mockTermRepo.Verify(repo => repo.DeleteByDocumentAsync(1), Times.Once);
    }
    
    [Fact]
    public async Task DocumentService_Delete_RemovesDocumentAndTerms()
    {
        // arrange - use an in-memory database
        var dbName = $"TestDatabase_{Guid.NewGuid()}";
        using var context = CreateInMemoryContext(dbName);

        // add a document directly to the database
        var docEntity = new DocumentEntity { Title = "Test Document" };
        context.Documents.Add(docEntity);
        await context.SaveChangesAsync();
        
        int docId = docEntity.Id;
        
        // create a document service with our mock repository
        var docService = CreateDocumentService(context);

        // act
        var result = await docService.DeleteAsync(docId);
        
        // assert
        Assert.True(result);

        // verify the document was removed
        var documentExists = await context.Documents.AnyAsync(d => d.Id == docId);
        Assert.False(documentExists);
    }
    
    [Fact]
    public async Task DocumentService_GetIndexedTokens_ReturnsCorrectTokens()
    {
        // arrange
        var mockDocRepo = new Mock<DocumentRepository>(new MockSearchEngineContext());
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        
        var expectedTokenMap = new Dictionary<string, List<int>>
        {
            { "test", new List<int> { 0 } },
            { "token", new List<int> { 1 } },
            { "retrieval", new List<int> { 2 } }
        };
        
        mockTermRepo.Setup(repo => repo.GetByDocumentAsync(It.IsAny<int>()))
            .ReturnsAsync(expectedTokenMap);
        
        var documentService = new DocumentService(mockDocRepo.Object, mockTermRepo.Object);
        
        // act
        var retrievedTokens = await documentService.GetIndexedTokensAsync(1);
        
        // assert
        Assert.Equal(3, retrievedTokens.Count);
        Assert.Contains(retrievedTokens, t => t.Term == "test");
        Assert.Contains(retrievedTokens, t => t.Term == "token");
        Assert.Contains(retrievedTokens, t => t.Term == "retrieval");
        
        mockTermRepo.Verify(repo => repo.GetByDocumentAsync(1), Times.Once);
    }
}