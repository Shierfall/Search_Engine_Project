using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using EFCore.BulkExtensions;
using SearchEngine.Analysis;
using SearchEngine.Persistence.Entities;
using System.Text.Json;
namespace SearchEngine.Persistence;

public class DocumentTermRepository
{
    private readonly SearchEngineContext _context;

    public DocumentTermRepository(SearchEngineContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Bulk‐upsert one row per distinct term for this document
    /// serializing all positions into JSON.
    /// </summary>
    public async Task BulkUpsertTermsAsync(int docId, IEnumerable<Token> tokens)
    {
        //  build the list of DocumentTermEntity
        var batch = tokens
            .GroupBy(t => t.Term)
            .Select(g => new DocumentTermEntity
            {
                DocumentId = docId,
                Term = g.Key,
                PositionsJson = JsonSerializer.Serialize(g.Select(t => t.Position))
            })
            .ToList();

        //  disable change‐tracking for max throughput
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        //  single bulk upsert call (INSERT or UPDATE as needed)
        await _context.BulkInsertOrUpdateAsync(batch);

        //  restore tracking
        _context.ChangeTracker.AutoDetectChangesEnabled = true;
        _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
    }

    /// <summary>
    /// Bulk load all term‐rows from the db in one go
    /// </summary>
    public async Task<List<DocumentTermEntity>> LoadAllTermsAsync()
    {
        return await _context.DocumentTerms
            .AsNoTracking()
            .ToListAsync();
    }
    /// <summary>
    /// Group the tokens by term, serialize their positions,
    /// and insert or update a single row per (docId,term).
    /// </summary>
    public async Task UpsertManyAsync(int docId, IEnumerable<Token> tokens)
    {
        // group positions in memory
        var groups = tokens
          .GroupBy(t => t.Term)
          .Select(g => new DocumentTermEntity
          {
              DocumentId = docId,
              Term = g.Key,
              PositionsJson = JsonSerializer.Serialize(g.Select(t => t.Position))
          })
          .ToList();

        foreach (var termRow in groups)
        {
            var existing = await _context.DocumentTerms
              .FindAsync(termRow.DocumentId, termRow.Term);

            if (existing == null)
            {
                _context.DocumentTerms.Add(termRow);
            }
            else
            {
                existing.PositionsJson = termRow.PositionsJson;
            }
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Delete *all* term rows for a document.
    /// </summary>
    public async Task DeleteByDocumentAsync(int docId)
    {
        var rows = await _context.DocumentTerms
          .Where(t => t.DocumentId == docId)
          .ToListAsync();

        if (rows.Any())
        {
            _context.DocumentTerms.RemoveRange(rows);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Load term rows for a doc and deserialize positions.
    /// </summary>
    public async Task<Dictionary<string, List<int>>> GetByDocumentAsync(int docId)
    {
        return await _context.DocumentTerms
          .AsNoTracking()
          .Where(t => t.DocumentId == docId)
          .ToDictionaryAsync(
            t => t.Term,
            t => JsonSerializer.Deserialize<List<int>>(t.PositionsJson)!);
    }
}
