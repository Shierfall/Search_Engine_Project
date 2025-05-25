using System.Collections.Generic;
using System.Threading.Tasks;
using SearchEngine.Analysis;
using SearchEngine.Persistence.Entities;

namespace SearchEngine.Services.Interfaces;

public interface IDocumentService
{
    /// <summary>
    /// Persist a new document, returning its generated ID.
    /// </summary>
    Task<int> CreateAsync(string title);

    /// <summary>
    /// Persist a new document with content, returning its generated ID.
    /// The content will be compressed before storage.
    /// </summary>
    Task<int> CreateWithContentAsync(string title, string content);

    /// <summary>
    /// Delete the document metadata; returns true if it existed.
    /// </summary>
    Task<bool> DeleteAsync(int docId);

    /// <summary>
    /// Fetch all documents (ID + Title).
    /// </summary>
    Task<IReadOnlyList<DocumentEntity>> GetAllAsync();

    /// <summary>
    /// Lookup a document's title by ID.
    /// </summary>
    Task<string> GetTitleAsync(int docId);

    /// <summary>
    /// Get the decompressed content of a document by ID.
    /// </summary>
    Task<string> GetContentAsync(int docId);

    /// <summary>
    /// Get the decompressed content of a document by title.
    /// </summary>
    Task<string> GetContentByTitleAsync(string title);

    /// <summary>
    /// Update the content of an existing document.
    /// The content will be compressed before storage.
    /// </summary>
    Task UpdateContentAsync(int docId, string content);

    /// <summary>
    /// Upsert one row per distinct term for this document,
    /// serializing all positions into JSON on the DB side.
    /// </summary>
    Task UpsertTermsAsync(int docId, IEnumerable<Token> tokens);

    /// <summary>
    /// Load the set of indexed tokens (one per term) for rebuilding in-memory indexes.
    /// </summary>
    Task<List<Token>> GetIndexedTokensAsync(int docId);

    /// <summary>
    /// Remove all term rows for the given document.
    /// </summary>
    Task DeleteTermsAsync(int docId);
}

