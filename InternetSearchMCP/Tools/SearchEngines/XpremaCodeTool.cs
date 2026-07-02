using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace InternetSearchMCP.Tools.SearchEngines;

public class XpremaCodeTool
{
    private const int GlobalCharBudget = 8000;

    [McpServerTool]
    [Description("XPREMA CODE: Searches the web for a solution to a coding problem or error and returns ONLY code snippets (formatting preserved), each with a single source-URL line. Use for compiler errors, exceptions, API usage questions, and how-to-implement problems. No prose is returned.")]
    public static async Task<string> XpremaCodeAsync(
        [Description("The coding problem, error message, or implementation question to solve.")] string problem,
        [Description("Optional programming language or framework hint (e.g. 'C#', 'ABP Framework', 'python').")] string language = "",
        [Description("How many top result pages to read (1-5, default 3).")] int maxSources = 3)
    {
        if (string.IsNullOrWhiteSpace(problem)) return "ERROR: Problem description cannot be empty.";
        maxSources = Math.Clamp(maxSources, 1, 5);

        string query = string.IsNullOrWhiteSpace(language) ? problem : $"{language} {problem}";

        List<SearchResultItem> results;
        try
        {
            results = await DuckDuckGoClient.SearchAsync(query);
        }
        catch (Exception ex)
        {
            return $"DIRECTIVE: Search request failed ({ex.Message}). Rephrase the problem and try again.";
        }

        if (results.Count == 0)
            return "DIRECTIVE: No search results found. Rephrase the problem using the exact error text or different keywords and call XpremaCodeAsync again.";

        var candidates = results.Take(maxSources).ToList();

        var fetches = candidates.Select(async r =>
        {
            string? html = Uri.TryCreate(r.Url, UriKind.Absolute, out var uri)
                ? await DuckDuckGoClient.FetchPageHtmlAsync(uri)
                : null;
            var blocks = html is null ? [] : DuckDuckGoClient.ExtractCodeBlocks(html);
            return (Result: r, Blocks: blocks);
        });
        var pages = await Task.WhenAll(fetches);

        var queryTerms = Tokenize(query);
        var identifiers = ExtractIdentifiers(problem);

        var scored = pages
            .SelectMany(p => p.Blocks.Select(b => (p.Result, Block: b, Score: Score(b, queryTerms, identifiers))))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Block.Length)
            .ToList();

        if (scored.Count == 0)
        {
            // Graceful degradation: no code anywhere — hand back the result list.
            return "DIRECTIVE: No code blocks found on the top result pages. Here are the raw results — pick a URL and use fetch_page_content, or rephrase and retry XpremaCodeAsync:\n" +
                   string.Join("\n", results.Take(10).Select((r, i) =>
                       $"[{i + 1}] {r.Title}\nURL: {r.Url}\n{(string.IsNullOrWhiteSpace(r.Snippet) ? "" : $"Snippet: {r.Snippet}\n")}---"));
        }

        var sb = new StringBuilder();
        int used = 0;
        foreach (var (result, block, _) in scored)
        {
            if (used + block.Length > GlobalCharBudget) continue;
            sb.AppendLine($"// Source: {result.Url}");
            sb.AppendLine("```");
            sb.AppendLine(block);
            sb.AppendLine("```");
            sb.AppendLine();
            used += block.Length;
            if (used >= GlobalCharBudget * 9 / 10) break;
        }

        return sb.ToString().TrimEnd();
    }

    private static double Score(string block, HashSet<string> queryTerms, List<string> identifiers)
    {
        var words = Tokenize(block);
        double score = queryTerms.Count == 0 ? 0 : (double)queryTerms.Count(words.Contains) / queryTerms.Count;

        // Boost blocks that mention exact identifiers from the problem (exception/type/method names)
        foreach (var id in identifiers)
        {
            if (block.Contains(id, StringComparison.Ordinal)) score += 0.5;
        }
        return score;
    }

    /// <summary>CamelCase/PascalCase or dotted identifiers from the problem text (e.g. NullReferenceException, HttpClient.PostAsync).</summary>
    private static List<string> ExtractIdentifiers(string problem) =>
        Regex.Matches(problem, @"\b[A-Za-z_][A-Za-z0-9_]*(?:[A-Z][a-z0-9]|\.[A-Za-z_])[A-Za-z0-9_.]*\b")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

    private static HashSet<string> Tokenize(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{2,}")
            .Select(m => m.Value)
            .ToHashSet();
}
