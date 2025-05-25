using SearchEngine.Analysis;
namespace SearchEngine.Core.Interfaces;

public interface IExactPrefixIndex
// might refactor (Add/Remove)Document to a separate interface
{
    ///<summary> 
    /// Adds data from <paramref name="docId"/> to data structure via <paramref name="tokens"/> stream
    ///</summary> 
    void AddDocument(int docId, IEnumerable<Token> tokens);

    ///<summary> 
    /// Recursively removes data from <paramref name="docId"/> to data structure via <paramref name="tokens"/> stream
    ///</summary> 
    void RemoveDocument(int docId, IEnumerable<Token> tokens);
    ///<summary> 
    ///returns true/false for <param name="term">word/string</param>
    ///</summary>
    bool Search(string term);
    ///<summary>
    ///same as IExactPrefix.Search(string term) but returns ids of documents where prefix appears
    ///</summary>
    List<int> PrefixSearchDocuments(string prefix);
    ///<summary>
    ///returns list of tuple of auto completion word and the ids of documents said word appears in 
    ///</summary>
    List<(string word, List<int> docIds)> PrefixSearch(string prefix);
    /// <summary>
    /// clear all in-memory data.
    /// </summary>
    void Clear();

    /// <summary>
    /// Search using boolean operators (naive implementation)
    /// </summary>
    List<(int docId, int count)> BooleanSearchNaive(string expr);
    
    /// <summary>
    /// Returns document IDs and counts for an exact search term, similar to inverted index
    /// </summary>
    List<(int docId, int count)> ExactSearchDocuments(string term);
}
