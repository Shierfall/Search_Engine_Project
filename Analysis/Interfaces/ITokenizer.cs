using System.Text.RegularExpressions;

namespace SearchEngine.Analysis.Interfaces;

public interface ITokenizer
{
    IEnumerable<Token> Tokenize(string text);
}



