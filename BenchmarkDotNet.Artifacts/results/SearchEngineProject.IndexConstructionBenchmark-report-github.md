```

BenchmarkDotNet v0.14.0, AlmaLinux 9.5 (Teal Serval)
Intel Xeon CPU E5-2670 v3 2.30GHz, 2 CPU, 24 logical and 24 physical cores
.NET SDK 8.0.406
  [Host]     : .NET 8.0.13 (8.0.1325.6609), X64 RyuJIT AVX2
  Job-UOWWVG : .NET 8.0.13 (8.0.1325.6609), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=1  

```
| Method                     | FileName  | Mean          | Error        | StdDev       | Gen0        | Gen1       | Gen2      | Allocated   |
|--------------------------- |---------- |--------------:|-------------:|-------------:|------------:|-----------:|----------:|------------:|
| **BenchmarkIndexConstruction** | **100KB.txt** |      **59.72 ms** |     **1.374 ms** |     **0.817 ms** |           **-** |          **-** |         **-** |     **50.8 MB** |
| **BenchmarkIndexConstruction** | **100MB.txt** | **494,489.26 ms** | **2,287.952 ms** | **1,513.339 ms** | **143000.0000** | **43000.0000** | **7000.0000** | **59770.91 MB** |
| **BenchmarkIndexConstruction** | **10MB.txt**  |   **9,565.98 ms** |   **102.586 ms** |    **67.854 ms** |  **15000.0000** |  **6000.0000** | **2000.0000** |  **5972.66 MB** |
| **BenchmarkIndexConstruction** | **1MB.txt**   |     **740.10 ms** |    **11.451 ms** |     **7.574 ms** |   **1000.0000** |          **-** |         **-** |   **593.28 MB** |
| **BenchmarkIndexConstruction** | **20MB.txt**  |  **26,280.30 ms** |   **155.359 ms** |    **92.451 ms** |  **32000.0000** | **11000.0000** | **4000.0000** |  **11972.8 MB** |
| **BenchmarkIndexConstruction** | **2MB.txt**   |   **1,466.77 ms** |    **15.829 ms** |    **10.470 ms** |   **2000.0000** |  **1000.0000** |         **-** |  **1182.29 MB** |
| **BenchmarkIndexConstruction** | **50MB.txt**  | **128,955.96 ms** | **1,224.650 ms** |   **810.030 ms** |  **75000.0000** | **23000.0000** | **5000.0000** | **29886.63 MB** |
| **BenchmarkIndexConstruction** | **5MB.txt**   |   **3,919.12 ms** |    **30.817 ms** |    **18.338 ms** |   **6000.0000** |  **2000.0000** |         **-** |  **2963.47 MB** |
