using BenchmarkDotNet.Running;

namespace SearchEngine.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        // run construction benchmark
        Console.WriteLine("Running Index Construction Benchmark...");
        BenchmarkRunner.Run<IndexConstructionBenchmark>();

        // run search operations benchmark
        Console.WriteLine("\nRunning Search Operations Benchmark...");
        BenchmarkRunner.Run<SearchOperationsBenchmark>();
    }
} 