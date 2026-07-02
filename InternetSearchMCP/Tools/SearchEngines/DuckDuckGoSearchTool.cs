using System.ComponentModel;
using ModelContextProtocol.Server;

namespace InternetSearchMCP.Tools.SearchEngines;

public class DuckDuckGoSearchTool
{
    private const int VisitedUrlsCap = 200;
    private static readonly HashSet<string> _visitedUrls = [];

    [McpServerTool]
    [Description("BROWSE STEP 1: Searches the web universally across any language. Returns absolute candidate hyperlinks. You must choose a URL from this list and pass it immediately to FetchPageContentAsync. For a one-shot consolidated answer, prefer SmartSearchAsync instead.")]
    public static async Task<string> SearchInternetAsync(
        [Description("The query string to look up on the web (supports English, Arabic, and any formatting styles).")] string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "ERROR: Search query cannot be empty.";

        try
        {
            var results = (await DuckDuckGoClient.SearchAsync(query)).Take(5).ToList();
            var unvisitedResults = results.Where(r => !_visitedUrls.Contains(r.Url)).ToList();

            if (unvisitedResults.Count == 0)
            {
                return "DIRECTIVE: No new links extracted. All options on this results sheet were explored or are invalid system URLs. You MUST trigger a brand new 'SearchInternetAsync' using modified keyword phrasings.";
            }

            return "CANDIDATE WEBPAGES DISCOVERED. Select the best unvisited link and call BROWSE STEP 2 (FetchPageContentAsync):\n" +
                   string.Join("\n", unvisitedResults.Select((r, i) => $"[{i + 1}] Title: {r.Title}\nURL: {r.Url}\n{(string.IsNullOrWhiteSpace(r.Snippet) ? "" : $"Snippet: {r.Snippet}\n")}---"));
        }
        catch (Exception ex)
        {
            return $"DIRECTIVE: Core network transport anomaly: {ex.Message}. Rephrase and try again.";
        }
    }

    [McpServerTool]
    [Description("BROWSE STEP 2: Scrapes clean, readable paragraph data lines from any URL. If this page context doesn't fully resolve the prompt, you must loop back, choose another link from the list, and execute this tool again.")]
    public static async Task<string> FetchPageContentAsync(
        [Description("The absolute HTTP/HTTPS destination URL address to parse.")] string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var targetUri) || (targetUri.Scheme != Uri.UriSchemeHttp && targetUri.Scheme != Uri.UriSchemeHttps))
        {
            return "DIRECTIVE: Bad URL construction passed. Select an alternate string directly from your latest candidate list.";
        }

        // Bounded de-dup tracking: clear when the cap is hit so long-running
        // sessions can never permanently exhaust all search results.
        if (_visitedUrls.Count >= VisitedUrlsCap) _visitedUrls.Clear();
        _visitedUrls.Add(url);

        try
        {
            string? plainText = await DuckDuckGoClient.FetchPageTextAsync(targetUri);

            if (string.IsNullOrWhiteSpace(plainText))
            {
                return "DIRECTIVE: This specific webpage layout returned empty or unreadable text layers. You have NOT found your answer yet. Loop back, choose an alternative target URL from your results list, and execute FetchPageContentAsync again.";
            }

            string extractedData = plainText.Length > 9000 ? plainText[..9000] + "\n...[Content truncated for length]..." : plainText;

            return $"--- WEBPAGE DATA EXTRACTED FROM: {url} ---\n" +
                   $"{extractedData}\n" +
                   $"-----------------------------------------\n" +
                   $"DIRECTIVE: Read the raw text block above. If it directly answers the user prompt, display your final structured results summary. If it is incomplete or ambiguous, continue looping through your remaining candidate URLs.";
        }
        catch (Exception ex)
        {
            return $"DIRECTIVE: Link retrieval failed ({ex.Message}). Choose an alternative target URL address from your active result parameters list.";
        }
    }
}
