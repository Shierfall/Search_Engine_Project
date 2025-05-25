using SearchEngine.Analysis.Interfaces;
using System.Collections;

namespace SearchEngine.Analysis.Filters;

public class SynonymFilter : ITokenFilter
{
    public readonly Dictionary<string, string[]> _synonyms;
    public SynonymFilter(Dictionary<string, string[]> synonyms)
    {
        _synonyms = synonyms;
    }

    public IEnumerable<Token> Filter(IEnumerable<Token> input)
    {
        foreach (var tok in input)
        {
            // // we always yield the og token
            // yield return tok;
            //  if we have synonyms for this term then we yield one token per synonym
            if (_synonyms.TryGetValue(tok.Term, out var arr))
            {
                foreach (var syn in arr)
                {
                    yield return new Token
                    {
                        Term = syn,
                        Position = tok.Position,
                        StartOffset = tok.StartOffset, // same text span, lets us highlight the word
                        EndOffset = tok.EndOffset
                    };
                }
            }
        }
    }
}
