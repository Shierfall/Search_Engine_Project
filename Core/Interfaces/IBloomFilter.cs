namespace SearchEngine.Core.Interfaces;

public interface IBloomFilter  
{  
    void Add(string term);  
    bool MightContain(string term);  
}  