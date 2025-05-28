using System.Collections.Generic;
using System.Threading.Tasks;
using SearchEngine.Analysis;

namespace SearchEngine.Persistence.Interfaces;

/// <summary>
/// Interface for document term repository operations
/// </summary>
public interface IDocumentTermRepository
{
    /// <summary>
    /// Bulk upsert terms for a document using optimized bulk operations
    /// </summary>
    Task BulkUpsertTermsAsync(int documentId, IEnumerable<Token> tokens);
    
    /// <summary>
    /// Delete all terms for a document
    /// </summary>
    Task DeleteTermsForDocumentAsync(int documentId);
    
    /// <summary>
    /// Delete all terms from the database
    /// </summary>
    Task DeleteAllTermsAsync();
    
    /// <summary>
    /// Get all terms for a document
    /// </summary>
    Task<Dictionary<string, List<int>>> GetByDocumentAsync(int documentId);
    
    /// <summary>
    /// Delete all terms for a document - alias for DeleteTermsForDocumentAsync
    /// </summary>
    Task DeleteByDocumentAsync(int documentId);
}
