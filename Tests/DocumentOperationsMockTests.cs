using Xunit;
using SearchEngine.Persistence;
using SearchEngine.Persistence.Entities;
using SearchEngine.Services;
using SearchEngine.Services.Interfaces;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Analysis;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Moq;
using SearchEngine.Tests.Mocks;

public class DocumentOperationsMockTests
{
    [Fact]
    public async Task IndexingService_RemoveDocument_UpdatesAllIndexes()
    {
        // arrange
        var mockDocService = new Mock<IDocumentService>();
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        var mockPrefixIndex = new Mock<IExactPrefixIndex>();
        var mockFullTextIndex = new Mock<IFullTextIndex>();
        var mockBloomFilter = new Mock<IBloomFilter>();
        var mockAnalyzer = new Mock<Analyzer>(MockBehavior.Loose, new object[] { null });
        
        // set up document service mock to return tokens
        var tokens = new List<Token> 
        {
            new Token { Term = "test", Position = 0 },
            new Token { Term = "document", Position = 1 }
        };
        
        // configure GetIndexedTokensAsync to be called before DeleteAsync
        var sequence = new MockSequence();
        mockDocService.InSequence(sequence)
            .Setup(s => s.GetIndexedTokensAsync(It.IsAny<int>()))
            .ReturnsAsync(tokens);
        
        mockDocService.InSequence(sequence)
            .Setup(s => s.DeleteAsync(It.IsAny<int>()))
            .ReturnsAsync(true);
        
        // create indexing service with mocks
        var indexingService = new IndexingService(
            mockAnalyzer.Object,
            mockDocService.Object,
            mockTermRepo.Object,
            new List<IExactPrefixIndex> { mockPrefixIndex.Object },
            mockFullTextIndex.Object,
            mockBloomFilter.Object
        );
        
        // act
        await indexingService.RemoveDocumentAsync(1);
        
        // assert - verify appropriate removal operations were called
        mockDocService.Verify(s => s.GetIndexedTokensAsync(1), Times.Once);
        mockPrefixIndex.Verify(idx => idx.RemoveDocument(1, It.IsAny<IEnumerable<Token>>()), Times.Once);
        mockFullTextIndex.Verify(idx => idx.RemoveDocument(1, It.IsAny<IEnumerable<Token>>()), Times.Once);
        // BloomFilter doesn't support removal
        mockDocService.Verify(s => s.DeleteAsync(1), Times.Once);
    }
    
    [Fact]
    public async Task RemoveDocument_WhenIndexDependenciesUpdated_DeletesFromDatabase()
    {
        // arrange
        var mockDocService = new Mock<IDocumentService>();
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        var mockPrefixIndex = new Mock<IExactPrefixIndex>();
        var mockFullTextIndex = new Mock<IFullTextIndex>();
        var mockBloomFilter = new Mock<IBloomFilter>();
        var mockAnalyzer = new Mock<Analyzer>(MockBehavior.Loose, new object[] { null });
        
        // set up specific call order expectations
        var callOrder = new List<string>();
        
        // configure GetIndexedTokensAsync to be called first
        mockDocService.Setup(s => s.GetIndexedTokensAsync(It.IsAny<int>()))
            .Callback(() => callOrder.Add("GetTokens"))
            .ReturnsAsync(new List<Token> { new Token { Term = "test" } });
        
        // set up behavior to track call order
        mockDocService.Setup(s => s.DeleteAsync(It.IsAny<int>()))
            .Callback(() => callOrder.Add("DeleteDoc"))
            .ReturnsAsync(true);
            
        mockFullTextIndex.Setup(idx => idx.RemoveDocument(It.IsAny<int>(), It.IsAny<IEnumerable<Token>>()))
            .Callback(() => callOrder.Add("FullText"));
            
        mockPrefixIndex.Setup(idx => idx.RemoveDocument(It.IsAny<int>(), It.IsAny<IEnumerable<Token>>()))
            .Callback(() => callOrder.Add("Prefix"));
           
        // create indexing service with mocks
        var indexingService = new IndexingService(
            mockAnalyzer.Object,
            mockDocService.Object,
            mockTermRepo.Object,
            new List<IExactPrefixIndex> { mockPrefixIndex.Object },
            mockFullTextIndex.Object,
            mockBloomFilter.Object
        );
        
        // act
        await indexingService.RemoveDocumentAsync(1);
        
        // assert
        // the first operation should be getting tokens
        Assert.Equal("GetTokens", callOrder[0]);
        // document deletion should be in the call order
        Assert.Contains("DeleteDoc", callOrder);
        // verify that prefix and full-text indexes are updated
        Assert.Contains("FullText", callOrder);
        Assert.Contains("Prefix", callOrder);
    }
    
    [Fact]
    public async Task DocumentService_DeleteAsync_RemovesTermsBeforeDocument()
    {
        // arrange - this test doesn't depend on IndexingService implementation
        var mockDocRepo = new Mock<DocumentRepository>(new MockSearchEngineContext());
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        
        var callOrder = new List<string>();
        
        // use sequence to enforce call order
        var sequence = new MockSequence();
        
        mockTermRepo.InSequence(sequence)
            .Setup(r => r.DeleteByDocumentAsync(It.IsAny<int>()))
            .Callback(() => callOrder.Add("DeleteTerms"))
            .Returns(Task.CompletedTask);
            
        mockDocRepo.InSequence(sequence)
            .Setup(r => r.DeleteAsync(It.IsAny<int>()))
            .Callback(() => callOrder.Add("DeleteDoc"))
            .ReturnsAsync(true);
        
        var documentService = new DocumentService(mockDocRepo.Object, mockTermRepo.Object);
        
        // act
        await documentService.DeleteAsync(1);
        
        // assert
        // check that both methods were called
        Assert.Equal(2, callOrder.Count);
        // check that DeleteTerms was called first
        Assert.Equal("DeleteTerms", callOrder[0]);
        // check that DeleteDoc was called second
        Assert.Equal("DeleteDoc", callOrder[1]);
    }
} 