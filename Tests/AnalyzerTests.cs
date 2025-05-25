using Xunit;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Filters;
using SearchEngine.Analysis.Tokenizers;
using Porter2StemmerStandard;
using System.Collections.Generic;
using System.Linq;

public class AnalyzerTests
{
    [Fact]
    public void Analyzer_WithMinimalTokenizer_TokenizesText()
    {
        // arrange
        var analyzer = new Analyzer(new MinimalTokenizer());
        var text = "This is a simple test.";

        // act
        var tokens = analyzer.Analyze(text).ToList();

        // assert
        Assert.Equal(5, tokens.Count);
        Assert.Equal("this", tokens[0].Term);
        Assert.Equal("is", tokens[1].Term);
        Assert.Equal("a", tokens[2].Term);
        Assert.Equal("simple", tokens[3].Term);
        Assert.Equal("test", tokens[4].Term);

        // check positions
        for (int i = 0; i < tokens.Count; i++)
        {
            Assert.Equal(i, tokens[i].Position);
        }
    }
    
    [Fact]
    public void Analyzer_WithStopWordFilter_RemovesStopWords()
    {
        // arrange
        var stopWords = new List<string> { "a", "the", "is" };
        var tokenizer = new MinimalTokenizer();
        var analyzer = new Analyzer(tokenizer, new StopWordFilter(stopWords));
        var text = "This is a simple test.";

        // act
        var tokens = analyzer.Analyze(text).ToList();

        // assert
        Assert.Equal(3, tokens.Count);
        Assert.Equal("this", tokens[0].Term);
        Assert.Equal("simple", tokens[1].Term);
        Assert.Equal("test", tokens[2].Term);
    }
    
    [Fact]
    public void Analyzer_WithPorterStemFilter_StemsTokens()
    {
        // arrange
        var stemmer = new EnglishPorter2Stemmer();
        var tokenizer = new MinimalTokenizer();
        var analyzer = new Analyzer(tokenizer, new PorterStemFilter(stemmer));
        var text = "Running and jumps are different forms";

        // act
        var tokens = analyzer.Analyze(text).ToList();

        // assert
        Assert.Equal(6, tokens.Count);
        Assert.Equal("run", tokens[0].Term);
        Assert.Equal("and", tokens[1].Term);
        Assert.Equal("jump", tokens[2].Term);
        Assert.Equal("are", tokens[3].Term);
        Assert.Equal("differ", tokens[4].Term);
        Assert.Equal("form", tokens[5].Term);
    }
    
    [Fact]
    public void Analyzer_WithMultipleFilters_AppliesFiltersInOrder()
    {
        // arrange
        var stemmer = new EnglishPorter2Stemmer();
        var stopWords = new List<string> { "and", "are", "the" };
        var synonyms = new Dictionary<string, string[]> { { "quick", new[] { "fast" } } };

        var tokenizer = new MinimalTokenizer();
        
        // create an analyzer with multiple filters
        var analyzer = new Analyzer(
            tokenizer,
            new StopWordFilter(stopWords),
            new SynonymFilter(synonyms)
        );
        
        var text = "The quick fox";
        
        // act
        var tokens = analyzer.Analyze(text).ToList();
        
        // assert
        Assert.Equal(1, tokens.Count);
        // "the" is removed by StopWordFilter, "quick" is replaced with "fast" by SynonymFilter
        Assert.Equal("fast", tokens[0].Term);
    }
    
    [Fact]
    public void Analyzer_WithStemAndKeepOriginalFilter_GeneratesMultipleTokens()
    {
        // arrange
        var stemmer = new EnglishPorter2Stemmer();
        var tokenizer = new MinimalTokenizer();
        var analyzer = new Analyzer(
            tokenizer,
            new StemAndKeepOriginalFilter(stemmer)
        );
        
        var text = "running jumping";
        
        // act
        var tokens = analyzer.Analyze(text).ToList();
        
        // assert
        Assert.Equal(4, tokens.Count); // 2 original + 2 stemmed versions
        Assert.Equal("running", tokens[0].Term);
        Assert.Equal("run", tokens[1].Term);
        Assert.Equal("jumping", tokens[2].Term);
        Assert.Equal("jump", tokens[3].Term);
        
        // the stemmed versions should have the same position as their originals
        Assert.Equal(tokens[0].Position, tokens[1].Position);
        Assert.Equal(tokens[2].Position, tokens[3].Position);
    }
    
    [Fact]
    public void Analyzer_PreservesPositionAndOffsetInformation()
    {
        // arrange
        var tokenizer = new MinimalTokenizer();
        var analyzer = new Analyzer(tokenizer);
        var text = "This is a test.";
        
        // act
        var tokens = analyzer.Analyze(text).ToList();
        
        // assert
        Assert.Equal(4, tokens.Count);
        
        // "This"
        Assert.Equal("this", tokens[0].Term);
        Assert.Equal(0, tokens[0].Position);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(4, tokens[0].EndOffset);
        
        // "is"
        Assert.Equal("is", tokens[1].Term);
        Assert.Equal(1, tokens[1].Position);
        Assert.Equal(5, tokens[1].StartOffset);
        Assert.Equal(7, tokens[1].EndOffset);
        
        // "a"
        Assert.Equal("a", tokens[2].Term);
        Assert.Equal(2, tokens[2].Position);
        Assert.Equal(8, tokens[2].StartOffset);
        Assert.Equal(9, tokens[2].EndOffset);
        
        // "test"
        Assert.Equal("test", tokens[3].Term);
        Assert.Equal(3, tokens[3].Position);
        Assert.Equal(10, tokens[3].StartOffset);
        Assert.Equal(14, tokens[3].EndOffset);
    }
} 