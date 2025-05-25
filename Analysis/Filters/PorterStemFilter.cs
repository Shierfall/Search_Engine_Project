using SearchEngine.Analysis.Interfaces;
using Porter2StemmerStandard;
namespace SearchEngine.Analysis.Filters;

public class PorterStemFilter : ITokenFilter
{
    private readonly EnglishPorter2Stemmer _stemmer;
    private readonly Dictionary<string, string> _stemCache = new(StringComparer.OrdinalIgnoreCase);

    public PorterStemFilter(EnglishPorter2Stemmer stemmer)
    {
        _stemmer = stemmer ?? throw new ArgumentNullException(nameof(stemmer));
    }

    public IEnumerable<Token> Filter(IEnumerable<Token> input)
    {
        foreach (var tok in input)
        {
            // skip empty or very short words
            if (string.IsNullOrWhiteSpace(tok.Term) || tok.Term.Length < 2)
            {
                yield return tok;
                continue;
            }

            var stemResult = StemOrGet(tok.Term);

            yield return new Token
            {
                Term = stemResult,
                Position = tok.Position,
                StartOffset = tok.StartOffset,
                EndOffset = tok.EndOffset
            };
        }
    }

    private string StemOrGet(string raw)
    {
        if (_stemCache.TryGetValue(raw, out var cached))
            return cached;
        
        // extra validation to ensure the word is valid for stemming
        if (raw.Length < 2)
        {
            _stemCache[raw] = raw;
            return raw;
        }

        try
        {
            var stemmed = _stemmer.Stem(raw).Value;
            _stemCache[raw] = stemmed;
            return stemmed;
        }
        catch (Exception)
        {
            // if stemming fails for any reason, return the original word
            _stemCache[raw] = raw;
            Console.WriteLine($"Stemming failed for word: {raw}");
            return raw;
        }
    }
}
