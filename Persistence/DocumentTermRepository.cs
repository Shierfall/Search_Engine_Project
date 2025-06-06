using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SearchEngine.Analysis;
using SearchEngine.Persistence.Entities;
using System.Text.Json;

namespace SearchEngine.Persistence;

/// <summary>
/// Repository class for managing document terms in the database
/// </summary>
public class DocumentTermRepository
{
    private readonly SearchEngineContext _context;
    private readonly ILogger<DocumentTermRepository> _logger;
    private const int SQL_BATCH_SIZE = 5000; // Maximum batch size for SQL Server bulk operations

    public DocumentTermRepository(SearchEngineContext context, ILogger<DocumentTermRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Bulk upsert terms for a document using optimized bulk operations
    /// </summary>
    public async Task BulkUpsertTermsAsync(int documentId, IEnumerable<Token> tokens)
    {
        if (!tokens.Any())
            return;
            
        // Delete existing terms for this document
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM DocumentTerms WHERE DocumentId = {0}", documentId);
            
        var termsList = tokens.ToList();
        
        // Use high-performance bulk insert when possible
        if (_context.Database.IsSqlServer() && termsList.Count > 100)
        {
            await BulkInsertSqlServerAsync(documentId, termsList);
        }
        else
        {
            await BulkInsertEfCoreAsync(documentId, termsList);
        }
    }
    
    /// <summary>
    /// Optimized bulk insert using SqlBulkCopy for SQL Server
    /// </summary>
    private async Task BulkInsertSqlServerAsync(int documentId, List<Token> tokens)
    {
        var connectionString = _context.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogWarning("Connection string not available, falling back to EF Core bulk insert");
            await BulkInsertEfCoreAsync(documentId, tokens);
            return;
        }
        
        using var dataTable = new DataTable();
        dataTable.Columns.Add("DocumentId", typeof(int));
        dataTable.Columns.Add("Term", typeof(string));
        dataTable.Columns.Add("PositionsJson", typeof(string));
        
        // Group tokens by term for position lists
        var groupedTerms = tokens
            .GroupBy(t => t.Term)
            .Select(g => new
            {
                Term = g.Key,
                Positions = g.Select(t => t.Position).ToList()
            });
            
        foreach (var term in groupedTerms)
        {
            var positionsJson = JsonSerializer.Serialize(term.Positions);
            dataTable.Rows.Add(documentId, term.Term, positionsJson);
        }
        
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        using var transaction = connection.BeginTransaction();
        try
        {
            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = "DocumentTerms",
                BatchSize = SQL_BATCH_SIZE,
                BulkCopyTimeout = 120 // seconds
            };
            
            bulkCopy.ColumnMappings.Add("DocumentId", "DocumentId");
            bulkCopy.ColumnMappings.Add("Term", "Term");
            bulkCopy.ColumnMappings.Add("PositionsJson", "PositionsJson");
            
            await bulkCopy.WriteToServerAsync(dataTable);
            transaction.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk insert of {Count} terms for document {DocumentId}", 
                tokens.Count, documentId);
            transaction.Rollback();
            throw;
        }
    }
    
    /// <summary>
    /// Fallback bulk insert using EF Core batching
    /// </summary>
    private async Task BulkInsertEfCoreAsync(int documentId, List<Token> tokens)
    {
        // group tokens by term
        var termGroups = tokens
            .GroupBy(t => t.Term)
            .Select(g => new
            {
                Term = g.Key,
                Positions = g.Select(t => t.Position).ToList()
            })
            .ToList();
            
        // process in chunks to avoid excessive memory usage
        var chunks = termGroups
            .Select((term, index) => new { term, index })
            .GroupBy(x => x.index / SQL_BATCH_SIZE)
            .Select(g => g.Select(x => x.term).ToList())
            .ToList();
            
        foreach (var chunk in chunks)
        {
            var entities = chunk.Select(term => new DocumentTermEntity
            {
                DocumentId = documentId,
                Term = term.Term,
                PositionsJson = JsonSerializer.Serialize(term.Positions)
            }).ToList();
            
            await _context.DocumentTerms.AddRangeAsync(entities);
            await _context.SaveChangesAsync();
            
            // Clear the context to prevent memory issues
            _context.ChangeTracker.Clear();
        }
    }
    
    /// <summary>
    /// Delete all terms for a document
    /// </summary>
    public async Task DeleteTermsForDocumentAsync(int documentId)
    {
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM DocumentTerms WHERE DocumentId = {0}", documentId);
    }
    
    /// <summary>
    /// Delete all terms from the database
    /// </summary>
    public async Task DeleteAllTermsAsync()
    {
        await _context.Database.ExecuteSqlRawAsync("DELETE FROM DocumentTerms");
    }
    
    /// <summary>
    /// Get all terms for a document
    /// </summary>
    public async Task<Dictionary<string, List<int>>> GetByDocumentAsync(int documentId)
    {
        var terms = await _context.DocumentTerms
            .AsNoTracking()
            .Where(t => t.DocumentId == documentId)
            .ToListAsync();
            
        var result = new Dictionary<string, List<int>>();
        
        foreach (var term in terms)
        {
            // Deserialize the positions from JSON
            var positions = JsonSerializer.Deserialize<List<int>>(term.PositionsJson) ?? new List<int>();
            result[term.Term] = positions;
        }
        
        return result;
    }

    /// <summary>
    /// Delete all terms for a document - alias for DeleteTermsForDocumentAsync
    /// </summary>
    public Task DeleteByDocumentAsync(int documentId)
    {
        return DeleteTermsForDocumentAsync(documentId);
    }
}
