using Microsoft.AspNetCore.Mvc;
using SearchEngine.Services.Interfaces;
using SearchEngine.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SearchEngine.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly ISearchService _searchService;
        private readonly FileContentService _fileContentService;
        private readonly IWikipediaService _wikipediaService;
        private readonly IIndexingService _indexingService;

        public SearchController(
            ISearchService searchService, 
            FileContentService fileContentService,
            IWikipediaService wikipediaService,
            IIndexingService indexingService)
        {
            _searchService = searchService;
            _fileContentService = fileContentService;
            _wikipediaService = wikipediaService;
            _indexingService = indexingService;
        }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Query parameter 'q' is required");
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var operationType = DetermineSearchOperation(q);
            var searchQuery = NormalizeQuery(q, operationType);
            
            Console.WriteLine($"Search request: '{q}', operation: '{operationType}', normalized: '{searchQuery}' (using CompactTrieIndex)");
            
            var results = await _searchService.SearchAsync(operationType, searchQuery);
            stopwatch.Stop();

            var response = new
            {
                results = await FormatResultsAsync(results, q),
                totalCount = GetResultCount(results),
                searchTime = stopwatch.Elapsed.TotalSeconds,
                query = q,
                operation = operationType
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Search error: {ex.Message}");
        }
    }

        [HttpGet("check")]
        public async Task<IActionResult> CheckExists([FromQuery] string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return BadRequest("Term parameter is required");
            }

            try
                {
                var result = await _searchService.SearchAsync("bloom", term);
                return Ok(new { exists = result });
            }
            catch (Exception ex)
            {
                return BadRequest($"Error checking term: {ex.Message}");
            }
        }

        [HttpGet("document/{title}")]
        public async Task<IActionResult> GetDocument(string title, [FromQuery] string searchTerm, [FromQuery] string operation)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return BadRequest("Title is required");
            }

            try
            {
                // try to get content from FileContentService first
                var content = await _fileContentService.GetDocumentContentAsync(title);
                
                // if no content is available, use hardcoded content based on document title
                if (string.IsNullOrEmpty(content))
                {
                    return NotFound("Document not found");
                }

                // if search term is provided, highlight all occurrences based on search operation
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    // clean the search term (remove special characters like * and #)
                    var cleanTerm = searchTerm.Replace("*", "").Replace("#", "");
                    
                    // handle different highlighting strategies based on search operation
                    switch (operation?.ToLower())
                    {
                        case "prefixdocs":
                            // for prefix search, highlight whole words that start with the term
                            var prefixPattern = $@"\b{Regex.Escape(cleanTerm)}[a-zA-Z0-9]*\b";
                            content = Regex.Replace(content, prefixPattern, "<mark>$&</mark>", RegexOptions.IgnoreCase);
                            break;
                            
                        case "fulltext" when searchTerm.Contains("&&") || searchTerm.Contains("||"):
                            // for boolean search, highlight each term separately
                            var terms = cleanTerm.Split(new[] { "&&", "||" }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(t => t.Trim())
                                                 .Where(t => !string.IsNullOrWhiteSpace(t));
                            
                            foreach (var term in terms)
                            {
                                var pattern = $@"\b{Regex.Escape(term)}\b";
                                content = Regex.Replace(content, pattern, "<mark>$&</mark>", RegexOptions.IgnoreCase);
                            }
                            break;
                            
                        case "fulltext" when searchTerm.Contains(" "):
                            // for phrase search, highlight ONLY the exact phrase, not individual words
                            var phrasePattern = Regex.Escape(cleanTerm);
                            content = Regex.Replace(content, phrasePattern, "<mark>$&</mark>", RegexOptions.IgnoreCase);
                            break;
                            
                        default:
                            // for exact search, highlight ONLY exact whole word matches with word boundaries
                            var exactPattern = $@"\b{Regex.Escape(cleanTerm)}\b";
                            content = Regex.Replace(content, exactPattern, match => {
                                // double-check that this is an exact match (not part of another word)
                                return match.Value.Equals(cleanTerm, StringComparison.OrdinalIgnoreCase) ? 
                                       $"<mark>{match.Value}</mark>" : match.Value;
                            }, RegexOptions.IgnoreCase);
                            break;
                    }
                }

                return Ok(new { title, content });
            }
            catch (Exception ex)
            {
                return BadRequest($"Error retrieving document: {ex.Message}");
            }
        }
        
        private string DetermineSearchOperation(string query)
        {
            // check for prefix search
            if (query.EndsWith("*"))
            {
                return "prefixDocs";
            }
            
            // check for boolean search (contains && or ||)
            if (query.Contains("&&") || query.Contains("||"))
            {
                return "fulltext";
            }
            
            // check for phrase search
            if (query.Contains(" "))
            {
                return "fulltext";
            }
            
            // default to exact search
            return "fulltext";
        }

        private string NormalizeQuery(string query, string operationType)
        {
            // handle the special character for prefix search
            if (operationType == "prefixDocs" && query.EndsWith("*"))
            {
                return query.Substring(0, query.Length - 1);
            }
            
            // we leave the rest of the normalization to the SearchService
            return query;
        }

        private async Task<List<object>> FormatResultsAsync(object rawResults, string originalQuery)
        {
            var formattedResults = new List<object>();
            var cleanQuery = originalQuery.Replace("*", ""); // clean query for highlighting
            var operationType = DetermineSearchOperation(originalQuery);
            
            if (rawResults is List<string> titles)
            {
                int total = titles.Count;
                
                var tasks = titles.Select(async (title, index) => {
                    // get snippet from file content using the correct search operation
                    string snippet = await _fileContentService.GetSnippetAsync(title, cleanQuery, operationType);
                    
                    return new
                    {
                        id = index.ToString(),
                        title = title,
                        snippet = snippet
                    };
                });
                
                formattedResults = (await Task.WhenAll(tasks)).ToList<object>();
            }
            else if (rawResults is List<int> docIds)
            {
                // for prefix search results (docIds only), don't include a score field
                if (docIds.Count == 0)
                {
                    return new List<object>();
                }
                
                var tasks = docIds.Select(async (docId, index) => {
                    var title = await _searchService.GetTitleAsync(docId);
                    string snippet = await _fileContentService.GetSnippetAsync(title, cleanQuery, operationType);
                    
                    return new
                    {
                        id = docId.ToString(),
                        title = title,
                        snippet = snippet
                    };
                });
                
                formattedResults = (await Task.WhenAll(tasks)).ToList<object>();
            }
            else if (rawResults is List<(int docId, double score)> scoredResults)
            {
                // these are already normalized scores from a ranked search
                try
                {
                    var tasks = scoredResults
                        .OrderByDescending(r => r.score)
                        .Select(async (result, index) => {
                            var title = await _searchService.GetTitleAsync(result.docId);
                            string snippet = await _fileContentService.GetSnippetAsync(title, cleanQuery, operationType);
                            
                            return new
                            {
                                id = result.docId.ToString(),
                                title = title,
                                snippet = snippet,
                                score = Math.Round(result.score, 1) // Round to one decimal place
                            };
                        });
                    
                    formattedResults = (await Task.WhenAll(tasks)).ToList<object>();
                }
                catch (Exception ex)
                {
                    // log the error but return an empty list
                    Console.Error.WriteLine($"Error formatting scored results: {ex.Message}");
                }
            }
            else if (rawResults is List<(int docId, int count)> countResults)
            {
                // for count-based results
                try
                {
                    var tasks = countResults
                        .OrderByDescending(r => r.count)
                        .Select<(int docId, int count), Task<object>>(async (result, index) => {
                            var title = await _searchService.GetTitleAsync(result.docId);
                            string snippet = await _fileContentService.GetSnippetAsync(title, cleanQuery, operationType);
                            
                            // include the count as score for BM25 results
                            return new
                            {
                                id = result.docId.ToString(),
                                title = title,
                                snippet = snippet,
                                score = result.count / 10.0 // convert to a normalized score
                            };
                        });
                    
                    formattedResults = (await Task.WhenAll(tasks)).ToList<object>();
                }
                catch (Exception ex)
                {
                    // log the error but return an empty list
                    Console.Error.WriteLine($"Error formatting count results: {ex.Message}");
                }
            }
            
            return formattedResults;
        }

        private int GetResultCount(object results)
        {
            if (results is List<string> titles)
            {
                return titles.Count;
            }
            else if (results is List<int> docIds)
            {
                return docIds.Count;
            }
            else if (results is List<(int docId, double score)> scoredResults)
            {
                return scoredResults.Count;
            }
            else if (results is List<(int docId, int count)> countResults)
            {
                return countResults.Count;
            }
            else if (results is bool)
            {
                return 0;
            }
            
            return 0;
        }
        
        [HttpGet("autocomplete")]
        public async Task<IActionResult> Autocomplete([FromQuery] string q)
        {
            try
            {
                // extract the last word from the query for autocomplete
                string lastWord = q;
                if (!string.IsNullOrWhiteSpace(q) && q.Contains(" "))
                {
                    // split by spaces and get the last word
                    string[] words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    lastWord = words.Length > 0 ? words[words.Length - 1] : "";
                }
                
                // log incoming request
                Console.WriteLine($"Autocomplete request for query: '{q}', using last word: '{lastWord}' (using CompactTrieIndex)");
                
                // use the autocomplete operation with just the last word
                var results = await _searchService.SearchAsync("autocomplete", lastWord ?? "");
                
                // check if we got a simple list of word suggestions (new format)
                if (results is List<string> wordCompletions)
                {
                    Console.WriteLine($"Found {wordCompletions.Count} word completions");
                    Console.WriteLine($"Results: {string.Join(", ", wordCompletions.Take(5))}...");
                    return Ok(wordCompletions.Take(20).ToList());
                }

                else if (results is List<(string word, List<string> titles)> acResultsWithTitles)
                {
                    var words = acResultsWithTitles
                        .Select(r => r.word)
                        .Distinct()
                        .Take(20)
                        .ToList();
                    
                    Console.WriteLine($"Extracted {words.Count} words from legacy format");
                    return Ok(words);
                }
                // fallback empty list
                else
                {
                    Console.WriteLine("No matching completions found");
                    return Ok(new List<string>());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Autocomplete error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
                // eturn an empty list in case of error
                return Ok(new List<string>());
            }
        }

    [HttpGet("bm25")]
    public async Task<IActionResult> BM25Search([FromQuery] string q, [FromQuery] double? k1, [FromQuery] double? b)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Query parameter 'q' is required");
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            // set BM25 parameters if provided
            if (k1.HasValue || b.HasValue)
            {
                await _searchService.SetBM25ParamsAsync(k1 ?? 1.2, b ?? 0.75);
            }
            
            Console.WriteLine($"BM25 search request: '{q}', params: k1={k1 ?? 1.2}, b={b ?? 0.75} (using CompactTrieIndex)");
            
            // always use the fulltext operation for BM25
            var results = await _searchService.SearchAsync("fulltext", q);
            stopwatch.Stop();
            
            // get current BM25 parameters
            var bm25Params = await _searchService.GetBM25ParamsAsync();

            var response = new
            {
                results = await FormatResultsAsync(results, q),
                totalCount = GetResultCount(results),
                searchTime = stopwatch.Elapsed.TotalSeconds,
                query = q,
                operation = "BM25",
                parameters = new
                {
                    k1 = bm25Params.k1,
                    b = bm25Params.b
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"BM25 search error: {ex.Message}");
        }
    }

        [HttpPost("wikipedia")]
        public async Task<IActionResult> AddFromWikipedia([FromQuery] string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return BadRequest("Title parameter is required");
            }

            try
            {
                // ensure database mode is enabled before adding Wikipedia content
                _fileContentService.EnableDatabaseMode();
                
                await _wikipediaService.AddFromWikipediaAsync(title);
                return Ok(new { message = $"Successfully added Wikipedia article: {title}" });
            }
            catch (Exception ex)
            {
                return BadRequest($"Error adding Wikipedia article: {ex.Message}");
            }
        }

        [HttpDelete("document/{docId:int}")]
        public async Task<IActionResult> DeleteDocument(int docId)
        {
            try
            {
                var result = await _indexingService.RemoveDocumentAsync(docId);
                
                if (result)
                {
                    return Ok(new { message = $"Document {docId} successfully deleted" });
                }
                else
                {
                    return NotFound(new { message = $"Document {docId} not found" });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"Error deleting document: {ex.Message}" });
            }
        }
    }
} 