namespace SearchEngine.Analysis;

public class Token
{
    public string Term { get; set; } = string.Empty;
    public int Position { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
}
