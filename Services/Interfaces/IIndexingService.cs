using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SearchEngine.Analysis;
using SearchEngine.Persistence;
using SearchEngine.Persistence.Entities;

namespace SearchEngine.Services.Interfaces;

public interface IIndexingService
{
    Task<int> AddDocumentAsync(string title, string content);
    Task IndexDocumentByIdAsync(int docId, string content);
    Task<bool> RemoveDocumentAsync(int docId);
    Task RebuildIndexAsync();
}
