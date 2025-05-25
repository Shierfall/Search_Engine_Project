namespace SearchEngine.Core.Interfaces;
/// <summary>
/// A single named search operation (exact, prefix‐docs, autocomplete…).
/// </summary>
public interface ISearchOperation
{
    /// <summary>
    /// Unique key used to select this op at runtime.
    /// e.g. "exact", "prefixDocs", "autocomplete".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Run the search. Returns:
    /// bool              for "exact"
    /// List<int>         for "prefixDocs"
    /// List<(string, List<int>)> for "autocomplete"
    /// </summary>
    Task<object> SearchAsync(string query);
}

