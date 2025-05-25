namespace SearchEngine.Persistence.Entities;

public class DocumentTokenEntity
{
    public int DocumentId { get; set; }
    public string Term { get; set; }
    public int Position { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }

    public DocumentEntity Document { get; set; }
}

