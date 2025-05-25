using SearchEngine.Analysis.Interfaces;
using Porter2StemmerStandard;
namespace SearchEngine.Analysis.Filters;

public class StemAndKeepOriginalFilter : ITokenFilter
{

    private readonly EnglishPorter2Stemmer _stemmer;
    public StemAndKeepOriginalFilter(EnglishPorter2Stemmer stemmer)
    {
        _stemmer = stemmer ?? throw new ArgumentNullException(nameof(stemmer));
    }
    public IEnumerable<Token> Filter(IEnumerable<Token> input)
    {
        foreach (var tok in input)
        {
            // yields the original token
            yield return tok;

            // yield the stemmed token at the same position
            // yielding both lets us preserve original word if it's needed (phrase search):w
            var stemResult = _stemmer.Stem(tok.Term).Value;

            yield return new Token
            {
                Term = stemResult,
                Position = tok.Position,
                StartOffset = tok.StartOffset,
                EndOffset = tok.EndOffset
            };
        }
    }
}
