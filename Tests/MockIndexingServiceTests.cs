using Xunit;
using SearchEngine.Persistence;
using SearchEngine.Services;
using SearchEngine.Services.Interfaces;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Tokenizers;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Moq;
using SearchEngine.Tests.Mocks;

public class MockIndexingServiceTests
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
        
        // configure the document service to return tokens
        var tokens = new List<Token>
        {
            new Token { Term = "test" },
            new Token { Term = "document" }
        };

        // configure GetIndexedTokensAsync to be called before DeleteAsync
        mockDocService.Setup(s => s.GetIndexedTokensAsync(1))
            .ReturnsAsync(tokens);
            
        mockDocService.Setup(s => s.DeleteAsync(1))
            .ReturnsAsync(true);
        
        var indexingService = new IndexingService(
            mockAnalyzer.Object,
            mockDocService.Object,
            mockTermRepo.Object,
            new List<IExactPrefixIndex> { mockPrefixIndex.Object },
            mockFullTextIndex.Object,
            mockBloomFilter.Object
        );
        
        // act
        var result = await indexingService.RemoveDocumentAsync(1);
        
        // assert
        Assert.True(result);
        mockDocService.Verify(s => s.GetIndexedTokensAsync(1), Times.Once);
        mockDocService.Verify(s => s.DeleteAsync(1), Times.Once);
        mockFullTextIndex.Verify(idx => idx.RemoveDocument(1, It.IsAny<IEnumerable<Token>>()), Times.Once);
        mockPrefixIndex.Verify(idx => idx.RemoveDocument(1, It.IsAny<IEnumerable<Token>>()), Times.Once);
        
        // Note: BloomFilter doesn't have a RemoveDocument method according to the interface
        // so we don't verify it here
    }
    
    [Fact]
    public async Task IndexingService_RemoveDocument_ReturnsFalse_WhenDocumentNotFound()
    {
        // arrange
        var mockDocService = new Mock<IDocumentService>();
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        var mockPrefixIndex = new Mock<IExactPrefixIndex>();
        var mockFullTextIndex = new Mock<IFullTextIndex>();
        var mockBloomFilter = new Mock<IBloomFilter>();
        var mockAnalyzer = new Mock<Analyzer>(MockBehavior.Loose, new object[] { null });
        
        // configure document service to return empty tokens (not found)
        mockDocService.Setup(s => s.GetIndexedTokensAsync(999))
            .ReturnsAsync(new List<Token>());
            
        // configure deletion to return false (not found)
        mockDocService.Setup(s => s.DeleteAsync(999)).ReturnsAsync(false);
        
        var indexingService = new IndexingService(
            mockAnalyzer.Object,
            mockDocService.Object,
            mockTermRepo.Object,
            new List<IExactPrefixIndex> { mockPrefixIndex.Object },
            mockFullTextIndex.Object,
            mockBloomFilter.Object
        );
        
        // act
        var result = await indexingService.RemoveDocumentAsync(999);
        
        // assert
        Assert.False(result);
        mockDocService.Verify(s => s.DeleteAsync(999), Times.Once);
    }
    
    [Fact]
    public async Task IndexingService_RemoveDocument_UpdatesIndexesWithCorrectTerms()
    {
        // arrange
        var mockDocService = new Mock<IDocumentService>();
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        var mockPrefixIndex = new Mock<IExactPrefixIndex>();
        var mockFullTextIndex = new Mock<IFullTextIndex>();
        var mockBloomFilter = new Mock<IBloomFilter>();
        var mockAnalyzer = new Mock<Analyzer>(MockBehavior.Loose, new object[] { null });
        
        // specific tokens to test
        var testTokens = new List<Token>
        {
            new Token { Term = "unique", Position = 0 },
            new Token { Term = "specific", Position = 1 }
        };
        
        // configure the document service to return our test tokens
        mockDocService.Setup(s => s.GetIndexedTokensAsync(1))
            .ReturnsAsync(testTokens);
            
        mockDocService.Setup(s => s.DeleteAsync(1))
            .ReturnsAsync(true);
        
        // configure the indexes to track what tokens they receive
        List<Token> tokensReceivedByFullText = null;
        mockFullTextIndex.Setup(idx => idx.RemoveDocument(1, It.IsAny<IEnumerable<Token>>()))
            .Callback<int, IEnumerable<Token>>((id, tokens) => tokensReceivedByFullText = tokens.ToList());
            
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
        
        // assert - verify the removal was done with the correct terms
        Assert.NotNull(tokensReceivedByFullText);
        Assert.Equal(testTokens.Count, tokensReceivedByFullText.Count);
        Assert.Contains(tokensReceivedByFullText, t => t.Term == "unique");
        Assert.Contains(tokensReceivedByFullText, t => t.Term == "specific");
    }
    
    [Fact]
    public async Task IndexingService_AddDocument_AddsToBloomFilter()
    {
        // arrange
        var mockDocService = new Mock<IDocumentService>();
        var mockTermRepo = new Mock<DocumentTermRepository>(new MockSearchEngineContext());
        var mockPrefixIndex = new Mock<IExactPrefixIndex>();
        var mockFullTextIndex = new Mock<IFullTextIndex>();
        var mockBloomFilter = new Mock<IBloomFilter>();

        // Create a real analyzer with a minimal tokenizer
        var analyzer = new Analyzer(new MinimalTokenizer());
        
        mockDocService.Setup(s => s.CreateAsync(It.IsAny<string>()))
            .ReturnsAsync(1);
        
        var indexingService = new IndexingService(
            analyzer,
            mockDocService.Object,
            mockTermRepo.Object,
            new List<IExactPrefixIndex> { mockPrefixIndex.Object },
            mockFullTextIndex.Object,
            mockBloomFilter.Object
        );
        
        // act
        await indexingService.AddDocumentAsync("Test", "bloom filter");
        
        // assert
        mockBloomFilter.Verify(f => f.Add("bloom"), Times.Once);
        mockBloomFilter.Verify(f => f.Add("filter"), Times.Once);
    }
} 