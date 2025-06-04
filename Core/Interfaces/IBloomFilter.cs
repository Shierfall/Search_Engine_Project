namespace SearchEngine.Core.Interfaces;

public interface IBloomFilter  
{  
    void Add(string term);  
    bool MightContain(string term);  
    
    // batch operations
    void AddBatch(IEnumerable<string> terms);
    Dictionary<string, bool> MightContainBatch(IEnumerable<string> terms);
}  