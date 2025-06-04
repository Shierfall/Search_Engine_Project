using System.Collections.Generic;
using System.Threading.Tasks;

namespace SearchEngine.Services.Interfaces;

public interface IIndexingService
{
    /// <summary>
    /// Adds a document to the search engine index
    /// </summary>
    /// <param name="title">The document title</param>
    /// <param name="content">The document content</param>
    /// <returns>The document ID</returns>
    Task<int> AddDocumentAsync(string title, string content);
    
    /// <summary>
    /// Removes a document from the search engine index
    /// </summary>
    /// <param name="docId">The document ID to remove</param>
    /// <returns>True if the document was found and removed, false otherwise</returns>
    Task<bool> RemoveDocumentAsync(int docId);
    
    /// <summary>
    /// Rebuilds the entire search index from the database
    /// </summary>
    Task RebuildIndexAsync();
    
    /// <summary>
    /// Indexes a document by its ID with the provided content
    /// </summary>
    /// <param name="docId">The document ID</param>
    /// <param name="content">The document content</param>
    Task IndexDocumentByIdAsync(int docId, string content);
    
    /// <summary>
    /// Indexes multiple documents in a batch 
    /// </summary>
    /// <param name="documents">List of document IDs and their content</param>
    Task IndexDocumentsBatchAsync(List<(int docId, string content)> documents);
}
