    using System;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using SearchEngine.Services.Interfaces;
    using System.Text.RegularExpressions;

    namespace SearchEngine.Services;

    public class WikipediaService : IWikipediaService
    {
        private readonly IIndexingService _indexingService;
        private readonly HttpClient _httpClient;
        private readonly FileContentService _fileContentService;

        public WikipediaService(
            IIndexingService indexingService, 
            IHttpClientFactory httpClientFactory,
            FileContentService fileContentService)
        {
            _indexingService = indexingService;
            _httpClient = httpClientFactory.CreateClient();
            _fileContentService = fileContentService;
        }

        public async Task AddFromWikipediaAsync(string title)
        {
            string url = $"https://en.wikipedia.org/w/api.php?action=query&prop=revisions&rvprop=content&rvslots=main&formatversion=2&redirects=1&format=json&titles={Uri.EscapeDataString(title)}";
            
            try
            {
                string json = await _httpClient.GetStringAsync(url);
                using JsonDocument doc = JsonDocument.Parse(json);
                var rootElement = doc.RootElement;

                if (rootElement.TryGetProperty("query", out var queryElement) &&
                    queryElement.TryGetProperty("pages", out var pagesElement) &&
                    pagesElement.GetArrayLength() > 0)
                {
                    var page = pagesElement[0];
                    
                    if (page.TryGetProperty("pageid", out var pageIdElement) && pageIdElement.GetInt32() == -1)
                    {
                        Console.WriteLine($"Page '{title}' not found or invalid.");
                        return;
                    }
                    if (page.TryGetProperty("revisions", out var revisionsElement) &&
                        revisionsElement.GetArrayLength() > 0)
                    {
                        var revision = revisionsElement[0];

                        if (revision.TryGetProperty("slots", out var slotsElement) &&
                            slotsElement.TryGetProperty("main", out var mainSlot) &&
                            mainSlot.TryGetProperty("content", out var contentElement))
                        {
                            string content = contentElement.GetString() ?? "";
                            
                            // clean Wikipedia syntax before storing
                            content = CleanWikipediaContent(content);

                            // register the document with FileContentService so content can be retrieved
                            _fileContentService.RegisterDocument(title, content);
                            
                            // use the existing indexing service to add the document
                            await _indexingService.AddDocumentAsync(title, content);
                            Console.WriteLine($"Added Wikipedia article: {title}");
                        }
                        else
                        {
                            Console.WriteLine($"No content found in main slot for '{title}'.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"No revisions found for '{title}'.");
                    }
                }
                else
                {
                    Console.WriteLine("No valid 'query'/'pages' data found in the response.");
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Error fetching data from Wikipedia: {e.Message}");
            }
        }
        
        private string CleanWikipediaContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;
                
            // remove all infobox templates (multiline)
            content = Regex.Replace(content, @"{{Infobox\s+[^}]*}}.*?}}", "", RegexOptions.Singleline);
            content = Regex.Replace(content, @"{{[Ii]nfobox\s.*?}}", "", RegexOptions.Singleline);
            
            // remove other templates with double braces
            content = Regex.Replace(content, @"{{[^{}]*?}}", "", RegexOptions.Singleline);
            content = Regex.Replace(content, @"{{.*?}}", "", RegexOptions.Singleline);
            
            // remove protection templates
            content = Regex.Replace(content, @"{{[Pp]rotection.*?}}", "", RegexOptions.Singleline);
            content = Regex.Replace(content, @"{{[Ss]hort description\|.*?}}", "", RegexOptions.Singleline);
            
            // remove category links
            content = Regex.Replace(content, @"\[\[Category:.*?\]\]", "");
            
            // remove file/image links
            content = Regex.Replace(content, @"\[\[File:.*?\]\]", "");
            content = Regex.Replace(content, @"\[\[Image:.*?\]\]", "");
            
            // remove HTML comments
            content = Regex.Replace(content, @"<!--.*?-->", "", RegexOptions.Singleline);
            
            // convert internal links to just their text
            content = Regex.Replace(content, @"\[\[([^|\]]*)\|([^]]*)\]\]", "$2"); // [[link|text]] -> text
            content = Regex.Replace(content, @"\[\[([^]]*)\]\]", "$1"); // [[link]] -> link
            
            // remove reference tags
            content = Regex.Replace(content, @"<ref[^>]*>.*?</ref>", "", RegexOptions.Singleline);
            content = Regex.Replace(content, @"<ref[^>]*/>", "");
            
            // remove external links
            content = Regex.Replace(content, @"\[http[^ ]* ([^\]]*)\]", "$1");
            content = Regex.Replace(content, @"\[(https?|ftp):\/\/[^\s\]]+\s([^\]]*)]", "$2");
            
            // remove formatting marks (bold, italic)
            content = Regex.Replace(content, @"'{2,}", "");
            
            // remove heading markers
            content = Regex.Replace(content, @"={2,}(.*?)={2,}", "$1");
            
            // remove any table markup
            content = Regex.Replace(content, @"{\|[\s\S]*?\|}", "", RegexOptions.Singleline);
            
            // remove any remaining XML/HTML tags
            content = Regex.Replace(content, @"<[^>]+>", "");
            
            // fix newlines
            content = content.Replace("\n\n\n", "\n\n");
            
            // remove URLs
            content = Regex.Replace(content, @"https?://\S+", "");
            
            // remove multiple spaces
            content = Regex.Replace(content, @"\s{2,}", " ");
            
            // :( 
            return content.Trim();
        }
    }
