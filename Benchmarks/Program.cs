using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using System;
namespace SearchEngine.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "memory-analysis":
                    Console.WriteLine("Running memory usage analysis across all file sizes...");
                    var indexBenchmark = new IndexConstructionBenchmark();
                    
                    // orocess each file size
                    foreach (var size in indexBenchmark.FileSizes)
                    {
                        indexBenchmark.FileSize = size;
                        indexBenchmark.Setup();
                        indexBenchmark.PrintMemoryUsage();
                        indexBenchmark.Cleanup();
                    }
                    break;
                    
                case "benchmark":
                    Console.WriteLine("Running Index Construction Benchmark...");
                    //BenchmarkRunner.Run<IndexConstructionBenchmark>(config);
                    
                    Console.WriteLine("\nRunning Search Operations Benchmark...");
                    BenchmarkRunner.Run<SearchOperationsBenchmark>(config);
                    break;
                    
                case "compression-stats":
                    Console.WriteLine("Analyzing document compression statistics...");
                    var compBenchmark = new CompressionBenchmark();
                    compBenchmark.PrintCompressionStats();
                    break;
                    
                case "delta-stats":
                    Console.WriteLine("Analyzing delta encoding statistics...");
                    var deltaBenchmark = new CompressionBenchmark();
                    deltaBenchmark.PrintDeltaEncodingStats();
                    break;
                    
                case "compression-benchmark":
                    Console.WriteLine("Running compression performance benchmarks...");
                    BenchmarkRunner.Run<CompressionBenchmark>(config);
                    break;
                    
                case "filter-analysis":
                    Console.WriteLine("Running filter analysis across all file sizes...");
                    var filterBenchmark = new FilterAnalysisBenchmark();
                    filterBenchmark.RunFilterAnalysis();
                    break;
                    
                default:
                    ShowHelp();
                    break;
            }
        }
        else
        {
            IndexConstructionBenchmark benchmark = new IndexConstructionBenchmark();
            benchmark.FileSize = "10MB"; 
            benchmark.Setup();
            benchmark.TrieConstruction();
            benchmark.InvertedIndexConstruction();
            benchmark.BloomFilterConstruction();
            benchmark.PrintMemoryUsage();
            
            ShowHelp();
        }
    }
    
    private static void ShowHelp()
    {
        Console.WriteLine("\nAvailable commands:");
        Console.WriteLine("  memory-analysis      - Analyze memory usage across all file sizes and export to CSV");
        Console.WriteLine("  benchmark            - Run full index construction and search operation benchmarks");
        Console.WriteLine("  compression-stats    - Analyze document compression statistics and export to CSV");
        Console.WriteLine("  delta-stats          - Analyze delta encoding statistics and export to CSV");
        Console.WriteLine("  compression-benchmark - Run performance benchmarks for compression and delta encoding");
        Console.WriteLine("  filter-analysis      - Analyze how different filter combinations affect memory usage");
    }
}