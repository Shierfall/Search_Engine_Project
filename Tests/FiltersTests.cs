using Xunit;
using SearchEngine.Analysis.Filters;
using SearchEngine.Analysis;
using System.Collections.Generic;
using Porter2StemmerStandard;
using System.Linq;

public class FiltersTests
{
    [Fact]
    public void StopWordFilter_RemovesStopWords()
    {
        // arrange
        var stopWords = new List<string> { "the", "and", "is" };
        var filter = new StopWordFilter(stopWords);
        var tokens = new List<Token>
        {
            new Token { Term = "this", Position = 0 },
            new Token { Term = "is", Position = 1 },
            new Token { Term = "a", Position = 2 },
            new Token { Term = "test", Position = 3 }
        };

        // act
        var result = filter.Filter(tokens).ToList();

        // assert
        Assert.DoesNotContain(result, t => t.Term == "is");
        Assert.Contains(result, t => t.Term == "this");
        Assert.Contains(result, t => t.Term == "a");
        Assert.Contains(result, t => t.Term == "test");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SynonymFilter_ReplacesSynonyms()
    {
        // checking the actual implementation to match whats expected
        var synonyms = new Dictionary<string, string[]> 
        { 
            { "quick", new[] { "fast" } },
            { "large", new[] { "big" } }
        };
        var filter = new SynonymFilter(synonyms);
        var tokens = new List<Token>
        {
            new Token { Term = "the", Position = 0 },
            new Token { Term = "quick", Position = 1 },
            new Token { Term = "fox", Position = 2 }
        };

        // act
        var result = filter.Filter(tokens).ToList();

        // assert
        // the SynonymFilter only outputs the synonyms, not the original tokens
        Assert.Contains(result, t => t.Term == "fast");
        Assert.Equal(1, result.Count); // only the synonym for "quick" is output
    }
    
    [Fact]
    public void PorterStemFilter_StemsWords_WithTokens()
    {
        // arrange
        var stemmer = new EnglishPorter2Stemmer();
        var filter = new PorterStemFilter(stemmer);
        var tokens = new List<Token>
        {
            new Token { Term = "running", Position = 0, StartOffset = 0, EndOffset = 7 },
            new Token { Term = "jumps", Position = 1, StartOffset = 8, EndOffset = 13 },
            new Token { Term = "easily", Position = 2, StartOffset = 14, EndOffset = 20 }
        };

        // act
        var result = filter.Filter(tokens).ToList();

        // assert
        Assert.Equal(3, result.Count);
        Assert.Equal("run", result[0].Term);
        Assert.Equal("jump", result[1].Term);
        Assert.Equal("easili", result[2].Term); // porter stemmer result

        // verify position and offsets are preserved
        Assert.Equal(0, result[0].Position);
        Assert.Equal(0, result[0].StartOffset);
        Assert.Equal(7, result[0].EndOffset);
    }
    
    [Fact]
    public void PorterStemFilter_HandlesShortWords()
    {
        // arrange
        var stemmer = new EnglishPorter2Stemmer();
        var filter = new PorterStemFilter(stemmer);
        var tokens = new List<Token>
        {
            new Token { Term = "a", Position = 0, StartOffset = 0, EndOffset = 1 },
            new Token { Term = "is", Position = 1, StartOffset = 2, EndOffset = 4 }
        };

        // act
        var result = filter.Filter(tokens).ToList();

        // assert
        Assert.Equal(2, result.Count);
        // short words should remain unchanged
        Assert.Equal("a", result[0].Term);
        Assert.Equal("is", result[1].Term);
    }
    
    [Fact]
    public void StemAndKeepOriginalFilter_KeepsBothForms()
    {
        // arrange
        var stemmer = new EnglishPorter2Stemmer();
        var filter = new StemAndKeepOriginalFilter(stemmer);
        var tokens = new List<Token>
        {
            new Token { Term = "running", Position = 0, StartOffset = 0, EndOffset = 7 },
            new Token { Term = "jumps", Position = 1, StartOffset = 8, EndOffset = 13 }
        };

        // act
        var result = filter.Filter(tokens).ToList();

        // assert
        Assert.Equal(4, result.Count); // should have twice as many tokens

        // check original forms
        Assert.Equal("running", result[0].Term);
        Assert.Equal("jumps", result[2].Term);

        // check stemmed forms
        Assert.Equal("run", result[1].Term);
        Assert.Equal("jump", result[3].Term);

        // check that position is preserved for stemmed forms
        Assert.Equal(0, result[0].Position);
        Assert.Equal(0, result[1].Position);
        Assert.Equal(1, result[2].Position);
        Assert.Equal(1, result[3].Position);
    }
}