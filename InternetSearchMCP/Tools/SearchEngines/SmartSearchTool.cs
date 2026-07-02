using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace InternetSearchMCP.Tools.SearchEngines;

public class SmartSearchTool
{
    private const int GlobalCharBudget = 8000;
    private const int MinParagraphLength = 40;

    [McpServerTool]
    [Description("SMART SEARCH: One-shot web search. Searches the web, fetches the top result pages in parallel, ranks their content against the query, and returns a single consolidated answer block with per-source attribution. Prefer this over the SearchInternetAsync/FetchPageContentAsync loop. Works in any language including Arabic.")]
    public static async Task<string> SmartSearchAsync(
        [Description("The query string to look up on the web (any language).")] string query,
        [Description("How many top result pages to read and consolidate (1-5, default 3).")] int maxSources = 3)
    {
        if (string.IsNullOrWhiteSpace(query)) return "ERROR: Search query cannot be empty.";
        maxSources = Math.Clamp(maxSources, 1, 5);

        List<SearchResultItem> results;
        try
        {
            results = await DuckDuckGoClient.SearchAsync(query);
        }
        catch (Exception ex)
        {
            return $"DIRECTIVE: Search request failed ({ex.Message}). Rephrase the query and try again.";
        }

        if (results.Count == 0)
            return "DIRECTIVE: No search results found. Rephrase the query using different keywords and call SmartSearchAsync again.";

        var candidates = results.Take(maxSources).ToList();

        var fetches = candidates.Select(async r =>
        {
            string? text = Uri.TryCreate(r.Url, UriKind.Absolute, out var uri)
                ? await DuckDuckGoClient.FetchPageTextAsync(uri)
                : null;
            return (Result: r, Text: text);
        });
        var pages = await Task.WhenAll(fetches);

        var successful = pages.Where(p => !string.IsNullOrWhiteSpace(p.Text)).ToList();

        if (successful.Count == 0)
        {
            // Graceful degradation: give the caller the result list to use with the low-level tools.
            return "DIRECTIVE: Could not read any of the top result pages. Here are the raw results — pick a URL and use FetchPageContentAsync, or rephrase and retry SmartSearchAsync:\n" +
                   string.Join("\n", results.Take(10).Select((r, i) =>
                       $"[{i + 1}] {r.Title}\nURL: {r.Url}\n{(string.IsNullOrWhiteSpace(r.Snippet) ? "" : $"Snippet: {r.Snippet}\n")}---"));
        }

        var queryTerms = Tokenize(query);
        int budgetPerSource = GlobalCharBudget / successful.Count;

        var sb = new StringBuilder();
        sb.AppendLine($"=== SMART SEARCH RESULTS FOR: {query} ===");
        for (int i = 0; i < successful.Count; i++)
        {
            var (result, text) = successful[i];
            sb.AppendLine();
            sb.AppendLine($"## Source [{i + 1}]: {result.Title} ({result.Url})");
            sb.AppendLine(SelectRelevantContent(text!, queryTerms, budgetPerSource));
        }
        sb.AppendLine();
        sb.AppendLine("DIRECTIVE: The content above comes from live web sources. Answer the user's question from it, citing sources by URL. If it is insufficient, call SmartSearchAsync again with a rephrased query.");
        return sb.ToString();
    }

    /// <summary>
    /// Splits page text into paragraphs and returns the most query-relevant ones
    /// (by term overlap), preserving original document order, within charBudget.
    /// </summary>
    private static string SelectRelevantContent(string text, HashSet<string> queryTerms, int charBudget)
    {
        var paragraphs = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length >= MinParagraphLength)
            .ToList();

        if (paragraphs.Count == 0)
            return text.Length <= charBudget ? text : text[..charBudget] + "…";

        var scored = paragraphs
            .Select((p, index) => (Paragraph: p, Index: index, Score: Score(p, queryTerms)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .ToList();

        var selected = new List<(string Paragraph, int Index)>();
        int used = 0;
        foreach (var item in scored)
        {
            if (used + item.Paragraph.Length > charBudget) continue;
            selected.Add((item.Paragraph, item.Index));
            used += item.Paragraph.Length;
            if (used >= charBudget * 9 / 10) break;
        }

        // Floor: always include at least the top of the page if nothing scored.
        if (selected.Count == 0)
        {
            string head = paragraphs[0];
            return head.Length <= charBudget ? head : head[..charBudget] + "…";
        }

        return string.Join("\n\n", selected.OrderBy(s => s.Index).Select(s => s.Paragraph));
    }

    private static double Score(string paragraph, HashSet<string> queryTerms)
    {
        if (queryTerms.Count == 0) return 0;
        var words = Tokenize(paragraph);
        int hits = queryTerms.Count(words.Contains);
        return (double)hits / queryTerms.Count;
    }

    /// <summary>Unicode-aware tokenization (\p{L}\p{N}) so Arabic and other scripts work.</summary>
    private static HashSet<string> Tokenize(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{2,}")
            .Select(m => m.Value)
            .ToHashSet();
}
