namespace SearchEngine.Analysis.Interfaces;

public interface ITokenFilter
{
    IEnumerable<Token> Filter(IEnumerable<Token> input);
}

