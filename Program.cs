using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite;
// using Microsoft.EntityFrameworkCore.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SearchEngine.Analysis;
using SearchEngine.Analysis.Interfaces;
using SearchEngine.Core;
using SearchEngine.Core.Interfaces;
using SearchEngine.Persistence;
using SearchEngine.Services;
using SearchEngine.Services.Interfaces;
using SearchEngine.Analysis.Tokenizers;
using SearchEngine.Analysis.Filters;
using Microsoft.AspNetCore.Builder;
using Porter2StemmerStandard;

// create the web application builder
var builder = WebApplication.CreateBuilder(args);

// get content file path if provide
string contentFilePath = args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? "";

// add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();//HttpClient factory for wikipedia
builder.Services.AddLogging();

// configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy => policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// add search operations
builder.Services.AddSingleton<ISearchOperation, ExactSearchOperation>();
builder.Services.AddSingleton<ISearchOperation>(sp =>
    new PrefixDocsSearchOperation(
        sp.GetRequiredService<IExactPrefixIndex>()
    )
);
builder.Services.AddSingleton<ISearchOperation, AutoCompleteSearchOperation>();
builder.Services.AddSingleton<ISearchOperation, FullTextSearchOperation>();
builder.Services.AddSingleton<ISearchOperation, BloomFilterSearchOperation>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IIndexingService, IndexingService>();

// register index implementations
builder.Services.AddSingleton<CompactTrieIndex>();
builder.Services.AddSingleton<IExactPrefixIndex>(provider => provider.GetRequiredService<CompactTrieIndex>());
builder.Services.AddSingleton<IFullTextIndex>(provider => provider.GetRequiredService<CompactTrieIndex>());
builder.Services.AddSingleton<IBloomFilter>(provider => new BloomFilter(100000, 0.01));

// add database context and repository services
builder.Services.AddDbContext<SearchEngineContext>(opts =>
    opts.UseSqlite("Data Source=quicktest.db"));
builder.Services.AddScoped<DocumentRepository>();
builder.Services.AddScoped<SearchEngine.Persistence.DocumentTermRepository>();
builder.Services.AddSingleton<DocumentCompressionService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

// add services
builder.Services.AddScoped<IWikipediaService, WikipediaService>();
builder.Services.AddSingleton<FileContentService>(sp =>
{
    // use the content file path we extracted
    var service = new FileContentService(contentFilePath, sp);
    // always enable database mode by default
    service.EnableDatabaseMode();
    return service;
});

// add analyzer pipeline
builder.Services.AddSingleton<Analyzer>(sp =>
    new Analyzer(
        new MinimalTokenizer()
    //,new PorterStemFilter(new EnglishPorter2Stemmer())
    )
);

var app = builder.Build();

// configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthorization();
app.MapControllers();

// initialize the database and preprocess data if command line args are provided
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SearchEngineContext>();
    dbContext.Database.EnsureCreated();

    var indexingService = scope.ServiceProvider.GetRequiredService<IIndexingService>();
    var fileContentService = scope.ServiceProvider.GetRequiredService<FileContentService>();
    var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();

    //if a file is specified, preprocess it
    if (!string.IsNullOrEmpty(contentFilePath))
    {
        Console.WriteLine($"Preprocessing {contentFilePath}…");
        var totalStopwatch = Stopwatch.StartNew();
        var processStages = new Dictionary<string, TimeSpan>();

        // enable database mode to store compressed content
        fileContentService.EnableDatabaseMode();

        // clear any existing document positions
        fileContentService.ClearDocumentPositions();

        using var reader = new StreamReader(contentFilePath, Encoding.UTF8);
        string? line;
        string? currentTitle = null;
        var sb = new StringBuilder();
        long startPos = 0;
        long currentPos = 0;

        var documentBatch = new List<(int docId, string content)>();
        var batchSize = 100;

        var docCounter = 0;
        var dbStopwatch = new Stopwatch();
        var tokenizeStopwatch = new Stopwatch();
        var indexStopwatch = new Stopwatch();

        while ((line = reader.ReadLine()) != null)
        {
            // track current position
            long lineLength = line.Length + Environment.NewLine.Length;

            if (currentTitle == null)
            {
                // first non-empty line is the title
                if (!string.IsNullOrWhiteSpace(line))
                {
                    currentTitle = line;
                    startPos = currentPos + lineLength; // start position after the title line
                }
            }
            else if (line.Trim() == "---END.OF.DOCUMENT---")
            {
                // end of document 
                if (sb.Length > 0)
                {
                    string content = sb.ToString().Trim();
                    docCounter++;

                    // Time document insertion to database
                    dbStopwatch.Start();
                    var docId = await documentService.CreateWithContentAsync(currentTitle, content);
                    dbStopwatch.Stop();

                    // add to batch instead of immediate indexing
                    documentBatch.Add((docId, content));

                    // process batch when it reaches the target size
                    if (documentBatch.Count >= batchSize)
                    {
                        // Time indexing operation
                        indexStopwatch.Start();
                        await indexingService.IndexDocumentsBatchAsync(documentBatch);
                        indexStopwatch.Stop();

                        documentBatch.Clear();
                        Console.WriteLine($"Processed batch of {batchSize} documents (total: {docCounter})");
                    }
                }

                // reset for next document
                currentTitle = null;
                sb.Clear();
            }
            else
            {
                // regular content line
                sb.AppendLine(line);
            }

            currentPos += lineLength;
        }

        // process final batch if any documents remain
        if (documentBatch.Count > 0)
        {
            indexStopwatch.Start();
            await indexingService.IndexDocumentsBatchAsync(documentBatch);
            indexStopwatch.Stop();
            Console.WriteLine($"Processed final batch of {documentBatch.Count} documents (total: {docCounter})");
        }

        totalStopwatch.Stop();

        // Display detailed timing information
        Console.WriteLine("\nTiming Information:");
        Console.WriteLine($"Database operations: {dbStopwatch.Elapsed.TotalSeconds:F3}s");
        Console.WriteLine($"Indexing operations: {indexStopwatch.Elapsed.TotalSeconds:F3}s");
        Console.WriteLine($"Total processing time: {totalStopwatch.Elapsed.TotalSeconds:F3}s");
        Console.WriteLine($"Average per document: {totalStopwatch.Elapsed.TotalMilliseconds / docCounter:F2}ms");
    }
    else
    {
        // No file specified, enable database mode and rebuild indexes
        fileContentService.EnableDatabaseMode();
        Console.WriteLine("Building search indexes from database...");
        var stopwatch = Stopwatch.StartNew();
        await indexingService.RebuildIndexAsync();
        stopwatch.Stop();
        Console.WriteLine($"Index rebuild time: {stopwatch.Elapsed.TotalSeconds:F3}s");
    }
}

// start the web application
using (var scope = app.Services.CreateScope())
{
    var invertedIndex = scope.ServiceProvider.GetRequiredService<IFullTextIndex>();
    if (invertedIndex is InvertedIndex bm25Index)
    {
        //change parameters using the /api/search/bm25 endpoint
        var (k1, b) = bm25Index.GetBM25Params();
        //Console.WriteLine($"BM25 parameters: k1={k1} b={b}");
        //Console.WriteLine("You can customize these parameters using the /api/search/bm25 endpoint");
    }
}
app.Run();

/* console application code
var host = Host.CreateDefaultBuilder(args)
               .ConfigureAppConfiguration((ctx, cfg) =>
               {
                   cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true) // check optional from storyline later
                      .AddEnvironmentVariables();
               })
               .ConfigureServices((ctx, services) =>
               {
                   //database and persistence
                   services.AddDbContext<SearchEngineContext>(opts => opts.UseSqlite("Data Source=quicktest.db"));
                   // opts.UseSqlServer(ctx.Configuration.GetConnectionString("SearchEngine")));
                   services.AddScoped<DocumentRepository>();
                   services.AddScoped<DocumentTermRepository>();
                   services.AddScoped<IDocumentService, DocumentService>();
                   var stopWords = new List<string>{"a", "the", "and", "or", "of", "on", "this", "we", "were",
                           "is", "not"};
                   services.AddScoped<ITokenFilter, StopWordFilter>();

                   //analysis pipeline
                   services.AddSingleton<Analyzer>(sp =>
                       new Analyzer(
                           new MinimalTokenizer()
                       )
                   );

                   //extensible search operations go here, facade pattern i think
                   services.AddSingleton<ISearchOperation, ExactSearchOperation>();
                   // register PrefixDocsSearchOperation with a factory to provide dependencies
                   services.AddSingleton<ISearchOperation>(sp => 
                       new PrefixDocsSearchOperation(
                           sp.GetRequiredService<IExactPrefixIndex>()
                       )
                   );
                   services.AddSingleton<ISearchOperation, AutoCompleteSearchOperation>();
                   services.AddSingleton<ISearchOperation, FullTextSearchOperation>();
                   services.AddSingleton<ISearchOperation, BloomFilterSearchOperation>();
                   services.AddScoped<ISearchService, SearchService>();

               })
               .Build();
using var scope = host.Services.CreateScope();
var docService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
var indexer = scope.ServiceProvider.GetRequiredService<IIndexingService>();
var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();

// ensure database is created
var dbContext = scope.ServiceProvider.GetRequiredService<SearchEngineContext>();
dbContext.Database.EnsureCreated();

if (args.Length < 1)
{
    Console.WriteLine("Usage: dotnet run <filename>");
    return;
}

var filename = args[0];
Console.WriteLine($"Preprocessing {filename}…");
var stopwatch = Stopwatch.StartNew();

using var reader = new StreamReader(filename, Encoding.UTF8);
string? line;
string? currentTitle = null;
bool titleRead = false;
var sb = new StringBuilder();

while ((line = reader.ReadLine()) != null)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    if (line == "---END.OF.DOCUMENT---")
    {
        if (titleRead && currentTitle is not null)
        {
            await indexer.AddDocumentAsync(currentTitle, sb.ToString());
        }

        titleRead = false;
        currentTitle = null;
        sb.Clear();
        continue;
    }

    if (!titleRead)
    {
        currentTitle = line;
        titleRead = true;
    }
    else
    {
        sb.AppendLine(line);
    }
}

// flush last document if file didn't end with the marker
if (titleRead && currentTitle is not null)
{
    await indexer.AddDocumentAsync(currentTitle, sb.ToString());
}

stopwatch.Stop();
Console.WriteLine($"Pre-process time: {stopwatch.Elapsed.TotalSeconds:F3}s");
Console.WriteLine("Ready for queries.  Type `<operation> <query>` or `exit`.");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var op = parts[0];
    var query = parts.Length > 1 ? parts[1] : "";

    try
    {
        var raw = await searchService.SearchAsync(op, query);

        switch (raw)
        {
            case bool b:
                Console.WriteLine(b);
                break;

            case List<string> list:
                Console.WriteLine(string.Join(", ", list));
                break;

            case List<int> docIds:
                Console.WriteLine($"Document IDs: {string.Join(", ", docIds)}");
                break;

            case List<(string word, List<string> titles)> ac:
                foreach (var (word, titles) in ac)
                {
                    Console.WriteLine($"{word}: {string.Join(", ", titles)}");
                }
                break;

            case List<(int docId, int count)> countResults:
                foreach (var (docId, count) in countResults.OrderByDescending(r => r.count))
                {
                    var title = await searchService.GetTitleAsync(docId);
                    Console.WriteLine($"{title} (ID: {docId}, Count: {count})");
                }
                break;

            case List<(int docId, double score)> scoredResults:
                foreach (var (docId, score) in scoredResults.OrderByDescending(r => r.score))
                {
                    var title = await searchService.GetTitleAsync(docId);
                    Console.WriteLine($"{title} (ID: {docId})");
                }
                break;

            default:
                Console.WriteLine(raw);
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

await host.StopAsync();
*/
