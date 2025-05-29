using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace SearchEngine.Benchmarks
{
    /// <summary>
    /// Helper utility for analyzing benchmark results
    /// </summary>
    public class BenchmarkAnalyzer
    {
        public static void AnalyzeResults(string resultsDir)
        {
            if (!Directory.Exists(resultsDir))
            {
                Console.WriteLine($"Results directory not found: {resultsDir}");
                return;
            }
            
            var files = Directory.GetFiles(resultsDir, "search-*.csv");
            if (files.Length == 0)
            {
                Console.WriteLine("No benchmark result files found.");
                return;
            }
            
            Console.WriteLine($"Found {files.Length} benchmark result files.");
            
            // Track performance by file size
            var sizePerformance = new Dictionary<string, Dictionary<string, double>>();
            
            foreach (var file in files)
            {
                var filename = Path.GetFileName(file);
                var sizePart = filename.Split('-')[1];
                
                Console.WriteLine($"\nAnalyzing results for {sizePart} from {filename}");
                
                try
                {
                    var lines = File.ReadAllLines(file);
                    if (lines.Length <= 1)
                    {
                        Console.WriteLine("  File appears to be empty or malformed.");
                        continue;
                    }
                    
                    // Parse header
                    var header = lines[0].Split(',');
                    var methodIndex = Array.IndexOf(header, "Method");
                    var meanIndex = Array.IndexOf(header, "Mean");
                    var errorIndex = Array.IndexOf(header, "Error");
                    var paramIndex = Array.IndexOf(header, "Params");
                    
                    if (methodIndex < 0 || meanIndex < 0)
                    {
                        Console.WriteLine("  Could not find required columns in CSV.");
                        continue;
                    }
                    
                    // Initialize size dictionary if needed
                    if (!sizePerformance.ContainsKey(sizePart))
                    {
                        sizePerformance[sizePart] = new Dictionary<string, double>();
                    }
                    
                    // Process result rows
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var cols = lines[i].Split(',');
                        if (cols.Length <= Math.Max(methodIndex, meanIndex))
                            continue;
                            
                        var method = cols[methodIndex].Trim();
                        if (string.IsNullOrEmpty(method))
                            continue;
                            
                        // Try to parse the mean value
                        if (double.TryParse(cols[meanIndex], NumberStyles.Any, CultureInfo.InvariantCulture, out double meanValue))
                        {
                            var operation = method;
                            if (paramIndex >= 0 && cols.Length > paramIndex)
                                operation = $"{method}-{cols[paramIndex].Trim('\"')}";
                                
                            sizePerformance[sizePart][operation] = meanValue;
                            
                            // Format performance for display
                            var errorStr = "";
                            if (errorIndex >= 0 && cols.Length > errorIndex && 
                                double.TryParse(cols[errorIndex], NumberStyles.Any, CultureInfo.InvariantCulture, out double errorValue))
                            {
                                errorStr = $" ± {errorValue:F2}";
                            }
                            
                            Console.WriteLine($"  {operation}: {meanValue:F2}{errorStr} ns");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error analyzing file: {ex.Message}");
                }
            }
            
            // Output a summary of performance across file sizes
            Console.WriteLine("\n=== Performance Summary ===");
            
            // Get common operations across all sizes
            var commonOperations = sizePerformance.Values
                .SelectMany(dict => dict.Keys)
                .GroupBy(key => key)
                .Where(g => g.Count() == sizePerformance.Count)
                .Select(g => g.Key)
                .ToList();
                
            foreach (var operation in commonOperations)
            {
                Console.WriteLine($"\nOperation: {operation}");
                Console.WriteLine("File Size   | Mean Performance (ns)");
                Console.WriteLine("------------|---------------------");
                
                foreach (var size in sizePerformance.Keys.OrderBy(SizeToBytes))
                {
                    var value = sizePerformance[size][operation];
                    Console.WriteLine($"{size,-12}| {value:N2}");
                }
            }
        }
        
        private static long SizeToBytes(string size)
        {
            if (size.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
                return long.Parse(size.Substring(0, size.Length - 2)) * 1024;
            if (size.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
                return long.Parse(size.Substring(0, size.Length - 2)) * 1024 * 1024;
            if (size.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
                return long.Parse(size.Substring(0, size.Length - 2)) * 1024 * 1024 * 1024;
            return 0;
        }
    }
}
