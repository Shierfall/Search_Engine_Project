namespace SearchEngine.Services.Interfaces;

public interface IWikipediaService
{
    Task AddFromWikipediaAsync(string title);
}