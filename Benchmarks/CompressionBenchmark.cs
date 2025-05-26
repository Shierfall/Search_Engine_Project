using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SearchEngine.Analysis;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Services;
using SearchEngine.Analysis.Tokenizers;
using System.Text;
using System.Collections;

namespace SearchEngine.Benchmarks;

[MemoryDiagnoser]
[WarmupCount(1)]
[IterationCount(5)]
public class CompressionBenchmark
{
    private string[] _fileSizes = new[] { "100KB", "1MB", "5MB", "10MB", "20MB" , "50MB", "100MB", "200MB", "400MB" };
    private string _basePath = "/home/shierfall/Downloads/texts/";
    private DocumentCompressionService _compressionService;
    private string _currentContent;
    private InvertedIndex _invertedIndexWithDelta;
    private InvertedIndex _invertedIndexNoDelta;
    private Analyzer _analyzer;

    [ParamsSource(nameof(FileSizes))]
    public string FileSize { get; set; }

    public IEnumerable<string> FileSizes => _fileSizes;

    [GlobalSetup]
    public void Setup()
    {
        try
        {
            Console.WriteLine($"Setting up benchmark for file size: {FileSize}");
            
            _compressionService = new DocumentCompressionService();
            _invertedIndexWithDelta = new InvertedIndex();
            _invertedIndexWithDelta.SetDeltaEncoding(true);
            
            _invertedIndexNoDelta = new InvertedIndex();
            _invertedIndexNoDelta.SetDeltaEncoding(false);
            
            _analyzer = new Analyzer(new MinimalTokenizer());

            string filePath = Path.Combine(_basePath, $"{FileSize}.txt");
            Console.WriteLine($"Loading file from: {filePath}");
            
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Warning: File not found at {filePath}");
                _currentContent = ""; // Empty content for non-existent file
            }
            else
            {
                _currentContent = File.ReadAllText(filePath);
                Console.WriteLine($"File loaded successfully, size: {_currentContent.Length} characters");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in Setup: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            _compressionService = new DocumentCompressionService();
            _invertedIndexWithDelta = new InvertedIndex();
            _invertedIndexNoDelta = new InvertedIndex();
            _analyzer = new Analyzer(new MinimalTokenizer());
            _currentContent = "";
        }
    }

    [Benchmark]
    public byte[] DocumentCompression()
    {
        return _compressionService.Compress(_currentContent);
    }

    [Benchmark]
    public string DocumentDecompression()
    {
        byte[] compressed = _compressionService.Compress(_currentContent);
        return _compressionService.Decompress(compressed);
    }

    [Benchmark]
    public void InvertedIndexWithDeltaEncoding()
    {
        var tokens = _analyzer.Analyze(_currentContent).ToList();
        _invertedIndexWithDelta.AddDocument(1, tokens);
    }

    [Benchmark]
    public void InvertedIndexWithoutDeltaEncoding()
    {
        var tokens = _analyzer.Analyze(_currentContent).ToList();
        _invertedIndexNoDelta.AddDocument(1, tokens);
    }

    public void PrintCompressionStats()
    {
        Console.WriteLine("\nCompression Statistics");
        Console.WriteLine("======================");
        
        var results = new List<(string FileSize, int OriginalSize, int CompressedSize, double CompressionRatio)>();
        
        foreach (var fileSize in _fileSizes)
        {
            try
            {
                string filePath = Path.Combine(_basePath, $"{fileSize}.txt");
                Console.WriteLine($"\nProcessing file: {fileSize} at {filePath}");
                
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Error: File not found at {filePath}");
                    continue;
                }
                
                string content = File.ReadAllText(filePath);
                Console.WriteLine($"File loaded, length: {content.Length} characters");
                
                // initialize compression service if null
                if (_compressionService == null)
                {
                    _compressionService = new DocumentCompressionService();
                    Console.WriteLine("Created new DocumentCompressionService");
                }
                
                byte[] originalBytes = Encoding.UTF8.GetBytes(content);
                Console.WriteLine($"Original size in bytes: {originalBytes.Length}");
                
                byte[] compressedBytes = _compressionService.Compress(content);
                Console.WriteLine($"Compressed size in bytes: {compressedBytes.Length}");
                
                double compressionRatio = (double)compressedBytes.Length / originalBytes.Length * 100;
                
                Console.WriteLine($"Original Size: {FormatBytes(originalBytes.Length)}");
                Console.WriteLine($"Compressed Size: {FormatBytes(compressedBytes.Length)}");
                Console.WriteLine($"Compression Ratio: {compressionRatio:F2}% ({(100 - compressionRatio):F2}% reduction)");
                
                results.Add((fileSize, originalBytes.Length, compressedBytes.Length, compressionRatio));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file {fileSize}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        if (results.Count > 0)
        {
            ExportCompressionResultsToCsv(results);
        }
        else
        {
            Console.WriteLine("No results to export");
        }
    }
    
    public void PrintDeltaEncodingStats()
    {
        Console.WriteLine("\nDelta Encoding Statistics");
        Console.WriteLine("=========================");
        
        var results = new List<(string FileSize, long PositionsWithDelta, long PositionsWithoutDelta, double SizeReduction, int TotalTokens, int UniqueTokens)>();
        
        foreach (var fileSize in _fileSizes)
        {
            try
            {
                string filePath = Path.Combine(_basePath, $"{fileSize}.txt");
                Console.WriteLine($"\nProcessing file: {fileSize}");
                
                string content = File.ReadAllText(filePath);
                var analyzer = new Analyzer(new MinimalTokenizer());
                var tokens = analyzer.Analyze(content).ToList();
                
                int totalTokens = tokens.Count;
                int uniqueTokens = tokens.Select(t => t.Term).Distinct().Count();
                
                Console.WriteLine($"Total tokens: {totalTokens}");
                Console.WriteLine($"Unique tokens: {uniqueTokens}");
                
                // create indices with delta encoding on and off
                var deltaIndex = new InvertedIndex();
                deltaIndex.SetDeltaEncoding(true);
                
                var noDeltaIndex = new InvertedIndex();
                noDeltaIndex.SetDeltaEncoding(false);
                
                deltaIndex.AddDocument(1, tokens);
                noDeltaIndex.AddDocument(1, tokens);
                

                long deltaPositionsSize = MeasurePositionStorage(deltaIndex);
                long noDeltaPositionsSize = MeasurePositionStorage(noDeltaIndex);
                
                double reduction = 0;
                if (noDeltaPositionsSize > 0)
                {
                    reduction = (double)(noDeltaPositionsSize - deltaPositionsSize) / noDeltaPositionsSize * 100;
                }
                
                Console.WriteLine($"Positions size with delta encoding: {FormatBytes(deltaPositionsSize)}");
                Console.WriteLine($"Positions size without delta encoding: {FormatBytes(noDeltaPositionsSize)}");
                Console.WriteLine($"Size reduction with delta encoding: {reduction:F2}%");
                
                results.Add((fileSize, deltaPositionsSize, noDeltaPositionsSize, reduction, totalTokens, uniqueTokens));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file {fileSize}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        ExportDeltaEncodingResultsToCsv(results);
    }
    
    private long MeasurePositionStorage(InvertedIndex index)
    {
        var searchTerms = new List<string>();
        var results = index.PrefixSearch("a");
        
        foreach (var (word, _) in results)
        {
            searchTerms.Add(word);
            if (searchTerms.Count >= 100) break; // limit to avoid excessive processing
        }
        

        if (searchTerms.Count == 0)
        {
            foreach (var prefix in new[] { "e", "t", "i", "o", "n", "s", "r" })
            {
                results = index.PrefixSearch(prefix);
                foreach (var (word, _) in results)
                {
                    searchTerms.Add(word);
                    if (searchTerms.Count >= 100) break;
                }
                if (searchTerms.Count >= 100) break;
            }
        }
        
        // if still no terms, we can't measure positions storage
        if (searchTerms.Count == 0)
        {
            return 0;
        }
        
        // now count the total storage used for positions
        long totalPositionsSize = 0;
        
        // for each term, calculate the total size using our new method
        foreach (var term in searchTerms)
        {
            totalPositionsSize += MeasurePositionsTotalBytes(index, term);
        }
        
        return totalPositionsSize;
    }
    
    private long CalculateInvertedIndexMemory(InvertedIndex invertedIndex)
    {
        // constants for object overhead
        const int OBJECT_OVERHEAD = 16; // object header (12) + padding (4)
        const int LIST_OVERHEAD = 32;   // object header + internal array reference + size fields
        const int DICT_OVERHEAD = 48;   // dictionary overhead
        
        long totalMemory = 0;
        
        // start with base object overhead
        totalMemory += OBJECT_OVERHEAD;
        
        // use reflection to access the private fields
        var type = invertedIndex.GetType();
        var fields = type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        foreach (var field in fields)
        {
            if (field.Name == "_map")
            {
                // Dictionary<string, List<Posting>>
                var map = field.GetValue(invertedIndex) as System.Collections.IDictionary;
                if (map != null)
                {
                    // Base dictionary overhead
                    long mapMemory = DICT_OVERHEAD;
                    
                    // Iterate through all entries
                    foreach (System.Collections.DictionaryEntry entry in map)
                    {
                        string key = entry.Key as string;
                        var postingsList = entry.Value as System.Collections.IList;
                        
                        // String key overhead + content
                        mapMemory += OBJECT_OVERHEAD + (key.Length * sizeof(char));
                        
                        // List<Posting> overhead
                        long postingsListMemory = LIST_OVERHEAD;
                        
                        foreach (var postingObj in postingsList)
                        {
                            var posting = postingObj;
                            // base Posting object overhead
                            long postingMemory = OBJECT_OVERHEAD;
                            
                            // get DocId, Count fields
                            postingMemory += sizeof(int) * 2;
                            
                            // get Positions list using reflection
                            var postingType = posting.GetType();
                            var positionsField = postingType.GetField("Positions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            var positions = positionsField.GetValue(posting) as System.Collections.IList;
                            
                            // List<int> overhead + content
                            postingMemory += LIST_OVERHEAD + (positions.Count * sizeof(int));
                            
                            postingsListMemory += postingMemory;
                        }
                        
                        mapMemory += postingsListMemory;
                    }
                    
                    totalMemory += mapMemory;
                }
            }
            else if (field.Name == "_bitIndex")
            {
                // Dictionary<string, BitArray>
                var bitIndex = field.GetValue(invertedIndex) as System.Collections.IDictionary;
                if (bitIndex != null)
                {
                    // base dictionary overhead
                    long bitIndexMemory = DICT_OVERHEAD;
                    
                    foreach (System.Collections.DictionaryEntry entry in bitIndex)
                    {
                        string key = entry.Key as string;
                        var bitArray = entry.Value as BitArray;
                        
                        // String key overhead + content
                        bitIndexMemory += OBJECT_OVERHEAD + (key.Length * sizeof(char));
                        
                        // BitArray overhead + storage
                        int intsRequired = (bitArray.Length + 31) / 32;
                        bitIndexMemory += OBJECT_OVERHEAD + sizeof(int) + OBJECT_OVERHEAD + (intsRequired * sizeof(int));
                    }
                    
                    totalMemory += bitIndexMemory;
                }
            }
            else if (field.Name == "_docLengths" || field.Name == "_docTitles")
            {
                // dictionaries
                var dict = field.GetValue(invertedIndex) as System.Collections.IDictionary;
                if (dict != null)
                {
                    totalMemory += DICT_OVERHEAD + (dict.Count * (sizeof(int) + IntPtr.Size));
                }
            }
            else
            {
                // basic fields
                if (field.FieldType == typeof(int) || field.FieldType == typeof(bool))
                    totalMemory += sizeof(int);
                else if (field.FieldType == typeof(double))
                    totalMemory += sizeof(double);
            }
        }
        
        return totalMemory;
    }
    
    private void ExportCompressionResultsToCsv(List<(string FileSize, int OriginalSize, int CompressedSize, double CompressionRatio)> results)
    {
        string csvFileName = $"document_compression_stats_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        using (var writer = new StreamWriter(csvFileName))
        {
            // write header
            writer.WriteLine("File Size,Original Size (Bytes),Original Size (Human),Compressed Size (Bytes),Compressed Size (Human),Compression Ratio,Reduction Percentage");
            
            // write data rows
            foreach (var result in results)
            {
                writer.WriteLine(
                    $"{result.FileSize}," +
                    $"{result.OriginalSize},{FormatBytes(result.OriginalSize)}," +
                    $"{result.CompressedSize},{FormatBytes(result.CompressedSize)}," +
                    $"{result.CompressionRatio:F2}%,{(100 - result.CompressionRatio):F2}%"
                );
            }
        }
        
        Console.WriteLine($"\nCompression results exported to {Path.GetFullPath(csvFileName)}");
    }
    
    private void ExportDeltaEncodingResultsToCsv(List<(string FileSize, long PositionsWithDelta, long PositionsWithoutDelta, double SizeReduction, int TotalTokens, int UniqueTokens)> results)
    {
        string csvFileName = $"delta_encoding_stats_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        using (var writer = new StreamWriter(csvFileName))
        {
            // write header
            writer.WriteLine("File Size,Total Tokens,Unique Tokens,Positions with Delta (Bytes),Positions with Delta (Human),Positions without Delta (Bytes),Positions without Delta (Human),Size Reduction Percentage");
            
            // write data rows
            foreach (var result in results)
            {
                writer.WriteLine(
                    $"{result.FileSize}," +
                    $"{result.TotalTokens},{result.UniqueTokens}," +
                    $"{result.PositionsWithDelta},{FormatBytes(result.PositionsWithDelta)}," +
                    $"{result.PositionsWithoutDelta},{FormatBytes(result.PositionsWithoutDelta)}," +
                    $"{result.SizeReduction:F2}%"
                );
            }
        }
        
        Console.WriteLine($"\nDelta encoding results exported to {Path.GetFullPath(csvFileName)}");
    }
    
    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number = number / 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }
    
    private List<int> GetPositionValuesForTerm(InvertedIndex index, string term, int docId)
    {
        try
        {
            // Now we can directly use the GetPositions method
            var positions = index.GetPositions(term, docId);
            
            // If no positions found, return empty list
            if (positions == null)
                return new List<int>();
                
            return positions;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error accessing position values: {ex.Message}");
            return new List<int>();
        }
    }
    
    private int CalculateVariableLengthIntSize(int value)
    {
        // this simulates a variable-length encoding scheme like VarInt or LEB128
        // where smaller integers take fewer bytes
        
        // make value positive if it's negative
        uint uValue = (uint)(value < 0 ? -value : value);
        
        if (uValue < 128) return 1;       // 1 byte for 0-127
        if (uValue < 16384) return 2;     // 2 bytes for 128-16383
        if (uValue < 2097152) return 3;   // 3 bytes for 16384-2097151
        if (uValue < 268435456) return 4; // 4 bytes for 2097152-268435455
        return 5;                         // 5 bytes for larger values
    }
    
    private long MeasurePositionsTotalBytes(IFullTextIndex index, string term)
    {
        long totalBytes = 0;
        var results = index.ExactSearch(term);
        
        foreach (var (docId, _) in results)
        {
            var positions = index.GetPositions(term, docId);
            if (positions != null && positions.Count > 0)
            {
                foreach (var position in positions)
                {
                    totalBytes += CalculateVariableLengthIntSize(position);
                }
            }
        }
        
        return totalBytes;
    }
}
