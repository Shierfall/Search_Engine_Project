using BenchmarkDotNet.Running;

namespace SearchEngine.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        // run the manual search benchmark
        ManualBenchmarks.ManualSearchBenchmark.RunBenchmark();
    }
}