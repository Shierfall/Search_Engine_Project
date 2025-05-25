using SearchEngine.Analysis;
namespace SearchEngine.Persistence.Entities;

public class DocumentEntity
{
    public int Id { get; set; }
    public string Title { get; set; }
    public byte[] CompressedContent { get; set; }
    public ICollection<DocumentTermEntity> Terms { get; set; } = new List<DocumentTermEntity>();
}

