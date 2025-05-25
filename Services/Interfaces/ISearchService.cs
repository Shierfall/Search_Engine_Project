using System.Collections.Generic;
using System.Threading.Tasks;

namespace SearchEngine.Services.Interfaces;

public interface ISearchService
{
    /// <summary>
    /// operation = "exact" | "prefixDocs" | "autocomplete" | any future search name
    /// </summary>
    Task<object> SearchAsync(string operation, string query);
    
    /// <summary>
    /// Gets the title of a document by its ID
    /// </summary>
    Task<string> GetTitleAsync(int documentId);
    
    /// <summary>
    /// Sets BM25 parameters for ranking
    /// </summary>
    /// <param name="k1">Term frequency saturation parameter (typically 1.2-2.0)</param>
    /// <param name="b">Document length normalization parameter (typically 0.75)</param>
    Task SetBM25ParamsAsync(double k1, double b);
    
    /// <summary>
    /// Gets current BM25 parameters
    /// </summary>
    /// <returns>Tuple with k1 and b parameters</returns>
    Task<(double k1, double b)> GetBM25ParamsAsync();
}


