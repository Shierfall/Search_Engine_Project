using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using SearchEngine.Benchmarks;

namespace SearchEngine.Tests
{
    public class BenchmarkTests
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Running search operation benchmark test...");
            
            var benchmark = new SearchOperationsBenchmark();
            benchmark.FileSize = "100KB"; // Use the smallest file for testing
            
            try
            {
                // Setup the benchmark
                benchmark.Setup();
                
                // Test a few search operations
                var exactResult = benchmark.TrieExactSearch("and");
                var prefixResult = benchmark.TriePrefixSearch("an");
                var phraseResult = benchmark.TriePhraseSearch("he was");
                
                Console.WriteLine($"Exact search found {exactResult.Count} results");
                Console.WriteLine($"Prefix search found {prefixResult.Count} results");
                Console.WriteLine($"Phrase search found {phraseResult.Count} results");
                
                // Cleanup
                benchmark.Cleanup();
                
                Console.WriteLine("Benchmark test completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running benchmark test: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
