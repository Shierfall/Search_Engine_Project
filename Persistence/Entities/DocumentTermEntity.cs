using SearchEngine.Analysis;
namespace SearchEngine.Persistence.Entities;

public class DocumentTermEntity
{
    public int DocumentId { get; set; }
    public string Term { get; set; }
    public string PositionsJson { get; set; }

    public DocumentEntity Document { get; set; }
}

