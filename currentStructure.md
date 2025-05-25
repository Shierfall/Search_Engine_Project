SearchEngine                            # root project directory
├── Analysis                          # text processing pipeline for tokenization and text normalization
│   ├── Analyzer.cs                   # combines tokenizers and filters into a processing pipeline
│   ├── Filters                       # directory containing various token filters
│   ├── Interfaces                    # interfaces for the analysis components
│   ├── SearchEngine.Analysis.csproj  # project file for the Analysis module
│   ├── Token.cs                      # represents a single unit of text (word)
│   └── Tokenizers                    # components that break text into individual tokens
├── appsettings.Development.json
├── appsettings.json
├── Benchmarks                        # performance testing code
│   ├── BenchmarkDotNet.Artifacts     # output from benchmark runs
│   ├── Benchmarks.csproj             # project file for benchmarks
│   ├── IndexConstructionBenchmark.cs # measures index building performance
│   ├── Program.cs                    # entry point for benchmark runs
│   └── SearchOperationsBenchmark.cs  # measures search operation performance
├── Controllers                       # API controllers that handle HTTP requests
│   └── SearchController.cs           # handles search-related API endpoints
├── Core                              # core search engine implementations
│   ├── AutoCompleteSearchOperation.cs # provides search term suggestions
│   ├── BloomFilter.cs                # probabilistic data structure for membership testing
│   ├── BloomFilterSearchOperation.cs # fast term existence checking
│   ├── CompactTrieIndex.cs           # efficient prefix tree for word lookups
│   ├── ExactSearchOperation.cs       # exact word matching
│   ├── FullTextSearchOperation.cs    # text search with relevance ranking
│   ├── Interfaces                    # core component interfaces
│   ├── InvertedIndex.cs              # maps terms to documents containing them
│   ├── PrefixDocumentsSearchOperation.cs # search by word prefixes
│   └── SearchEngine.Core.csproj      # project file for Core module
├── currentStructure.md               # this file - documents project structure
├── frontend                          # user interface code
│   ├── index.html                    # main HTML entry point
│   ├── package.json                  # npm dependencies
│   ├── package-lock.json             # exact dependency versions
│   ├── postcss.config.js             # CSS processing configuration
│   ├── public                        # static assets
│   ├── src                           # frontend source code
│   ├── tailwind.config.js            # Tailwind CSS configuration
│   ├── tsconfig.json                 # TypeScript configuration
│   ├── tsconfig.node.json            # TypeScript config for Node environment
│   └── vite.config.ts                # Vite bundler configuration
├── Persistence                       # data storage layer
│   ├── DocumentRepository.cs         # handles document storage operations
│   ├── DocumentTermRepository.cs     # manages term-to-document relationships
│   ├── Entities                      # database entity models
│   ├── Interfaces                    # persistence layer interfaces
│   ├── Persistence                   # additional persistence utilities
│   ├── SearchEngineContext.cs        # database context for Entity Framework
│   └── SearchEngine.Persistence.csproj # project file for Persistence module
├── Program.cs                        # application entry point and startup configuration
├── Properties                        # project properties
│   └── launchSettings.json           # runtime configuration
├── quicktest.db                      # SQLite database file
├── README.md                         # project documentation
├── SearchEngine.csproj               # main project file
├── SearchEngine.sln                  # solution file
├── Services                          # business logic layer
│   ├── DocumentCompressionService.cs # compresses document content
│   ├── DocumentService.cs            # manages document operations
│   ├── FileContentService.cs         # handles file I/O operations
│   ├── IndexingService.cs            # manages index building and updates
│   ├── Interfaces                    # service interfaces
│   ├── SearchEngine.Services.csproj  # project file for Services module
│   ├── SearchService.cs              # coordinates search operations
│   └── WikipediaService.cs           # imports content from Wikipedia
└── Tests                             # automated tests
    ├── AnalyzerTests.cs              # tests for text analysis components
    ├── DocumentOperationsMockTests.cs # tests document operations with mocks
    ├── DocumentRemovalTests.cs       # tests document deletion functionality
    ├── FiltersTests.cs               # tests token filter components
    ├── IndexingTests.cs              # tests indexing operations
    ├── MockIndexingServiceTests.cs   # tests indexing with mock services
    ├── Mocks                         # mock implementations for testing
    ├── SearchEngine.Tests.csproj     # test project file
    ├── SearchOperationsTests.cs      # tests search functionality
    └── TestHelpers.cs                # utility code for tests