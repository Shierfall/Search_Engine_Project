using SearchEngine.Analysis.Interfaces;
using Porter2StemmerStandard;
using System.Collections.Concurrent;
namespace SearchEngine.Analysis.Filters;

public class PorterStemFilter : ITokenFilter
{
    private readonly EnglishPorter2Stemmer _stemmer;
    private readonly ConcurrentDictionary<string, string> _stemCache = new(StringComparer.OrdinalIgnoreCase);

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
        return _stemCache.GetOrAdd(raw, key =>
        {
            // extra validation to ensure the word is valid for stemming
            if (key.Length < 2)
            {
                return key;
            }

            try
            {
                return _stemmer.Stem(key).Value;
            }
            catch (Exception)
            {
                // if stemming fails for any reason, return the original word
                Console.WriteLine($"Stemming failed for word: {key}");
                return key;
            }
        });
    }
}
