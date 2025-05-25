using System.Collections.Generic;
using System.Threading.Tasks;
using SearchEngine.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace SearchEngine.Persistence;


public class DocumentRepository
{
    private readonly SearchEngineContext _context;

    public DocumentRepository(SearchEngineContext context)
    {
        _context = context;
    }
    /// <summary>
    /// Inserts a new document with the given title.
    /// Returns the generated document Id.
    /// </summary>
    public async Task<int> InsertAsync(string title)
    {
        var doc = new DocumentEntity { Title = title };
        _context.Documents.Add(doc);
        await _context.SaveChangesAsync();
        return doc.Id;
    }

    /// <summary>
    /// Inserts a new document with the given title and compressed content.
    /// Returns the generated document Id.
    /// </summary>
    public async Task<int> InsertWithContentAsync(string title, byte[] compressedContent)
    {
        var doc = new DocumentEntity 
        { 
            Title = title,
            CompressedContent = compressedContent
        };
        _context.Documents.Add(doc);
        await _context.SaveChangesAsync();
        return doc.Id;
    }

    /// <summary>
    /// Updates the compressed content for an existing document.
    /// </summary>
    public async Task UpdateContentAsync(int docId, byte[] compressedContent)
    {
        var doc = await _context.Documents.FindAsync(docId);
        if (doc != null)
        {
            doc.CompressedContent = compressedContent;
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Retrieves the compressed content for a document.
    /// </summary>
    public async Task<byte[]> GetCompressedContentAsync(int docId)
    {
        var doc = await _context.Documents.FindAsync(docId);
        return doc?.CompressedContent;
    }

    /// <summary>
    /// Retrieves the compressed content for a document by title.
    /// </summary>
    public async Task<byte[]> GetCompressedContentByTitleAsync(string title)
    {
        var doc = await _context.Documents
                               .FirstOrDefaultAsync(d => d.Title == title);
        return doc?.CompressedContent;
    }

    /// <summary>
    /// Deletes the document with the given Id.
    /// Returns true if a row was deleted.
    /// </summary>
    public async Task<bool> DeleteAsync(int docId)
    {
        var doc = await _context.Documents.FindAsync(docId);
        if (doc == null)
        {
            return false;
        }
        // delete by document 
        _context.Documents.Remove(doc);
        await _context.SaveChangesAsync();
        return true;
    }
    /// <summary>
    /// Retrieves the title for a given document Id, or null if not found.
    /// </summary>
    public async Task<string> GetTitleAsync(int docId)
    {
        var doc = await _context.Documents.FindAsync(docId);
        return doc?.Title; // change this later so Title is never null
    }

    /// <summary>
    /// Returns all documents with their Id and Title
    /// <summary/>
    public async Task<IReadOnlyList<DocumentEntity>> GetAllAsync()
    {
        return await _context.Documents
                             .AsNoTracking()
                             .ToListAsync();
    }
}


