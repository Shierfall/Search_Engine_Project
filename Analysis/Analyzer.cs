using System.Collections.Generic;
using System.Linq;
using SearchEngine.Analysis.Interfaces;

namespace SearchEngine.Analysis;

public class Analyzer
{
    private readonly ITokenizer _tokenizer;
    private readonly List<ITokenFilter> _filters;

    public Analyzer(ITokenizer tokenizer, params ITokenFilter[] filters)
    {
        _tokenizer = tokenizer;
        _filters = filters.ToList();
    }

    public IEnumerable<Token> Analyze(string text)
    {
        var stream = _tokenizer.Tokenize(text);
        foreach (var filter in _filters)
        {
            stream = filter.Filter(stream);
        }
        return stream;
    }
}
