using System.Linq;
using SearchEngine.Analysis.Interfaces;
namespace SearchEngine.Analysis.Filters;

public class StopWordFilter : ITokenFilter
{

    private readonly HashSet<string> _stops;

    public StopWordFilter(IEnumerable<string> stops)
    {
        _stops = new HashSet<string>(stops);
    }

    public IEnumerable<Token> Filter(IEnumerable<Token> input)
    {
        return input.Where(t => !_stops.Contains(t.Term));
    }
}

