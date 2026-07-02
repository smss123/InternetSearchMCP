using System.Net;
using System.Text.RegularExpressions;

namespace InternetSearchMCP.Tools.SearchEngines;

/// <summary>
/// Shared search/fetch core used by both the low-level browse tools and SmartSearch.
/// </summary>
internal static class DuckDuckGoClient
{
    private const int PageFetchTimeoutSeconds = 10;
    private const int MaxPageBytes = 1_000_000;

    internal static readonly HttpClient Http;

    static DuckDuckGoClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        Http = new HttpClient(handler);
        Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        Http.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    }

    /// <summary>
    /// Searches the DuckDuckGo HTML endpoint and returns only organic results.
    /// Throws HttpRequestException on non-success status.
    /// </summary>
    internal static async Task<List<SearchResultItem>> SearchAsync(string query)
    {
        var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("q", query)]);
        var response = await Http.PostAsync("https://html.duckduckgo.com/html/", content);
        response.EnsureSuccessStatusCode();

        string html = await response.Content.ReadAsStringAsync();
        return ParseOrganicResults(html);
    }

    /// <summary>
    /// Parses organic results (result__a anchors + result__snippet) from the
    /// html.duckduckgo.com results page, resolving uddg= redirect wrappers.
    /// </summary>
    internal static List<SearchResultItem> ParseOrganicResults(string html)
    {
        var items = new List<SearchResultItem>();

        var resultBlocks = Regex.Matches(html,
            @"<a[^>]*class=""[^""]*result__a[^""]*""[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase);

        var snippets = Regex.Matches(html,
            @"<a[^>]*class=""[^""]*result__snippet[^""]*""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase);

        for (int i = 0; i < resultBlocks.Count; i++)
        {
            var m = resultBlocks[i];
            string link = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
            string title = WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, "<[^>]*>", "")).Trim();
            string snippet = i < snippets.Count
                ? WebUtility.HtmlDecode(Regex.Replace(snippets[i].Groups[1].Value, "<[^>]*>", "")).Trim()
                : "";

            // Resolve DuckDuckGo redirect wrapper to the real destination URL
            var uddg = Regex.Match(link, @"uddg=([^&]+)");
            if (uddg.Success)
                link = Uri.UnescapeDataString(uddg.Groups[1].Value);

            if (Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                !uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(title) &&
                !items.Any(x => x.Url == link))
            {
                items.Add(new SearchResultItem(title, link, snippet));
            }
        }

        return items;
    }

    /// <summary>
    /// Fetches a page (with timeout and size cap) and returns cleaned plain text,
    /// or null if the fetch failed or yielded no readable text.
    /// </summary>
    internal static async Task<string?> FetchPageTextAsync(Uri url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(PageFetchTimeoutSeconds));
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            var buffer = new char[MaxPageBytes];
            int read = await reader.ReadBlockAsync(buffer, cts.Token);
            string html = new(buffer, 0, read);

            string text = HtmlToPlainText(html);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts code blocks (&lt;pre&gt; contents, then standalone multi-line &lt;code&gt;)
    /// preserving indentation and line breaks. Returns deduped blocks in page order.
    /// </summary>
    internal static List<string> ExtractCodeBlocks(string html, int minLength = 30)
    {
        var blocks = new List<string>();
        var seen = new HashSet<string>();

        void Add(string raw)
        {
            string code = Regex.Replace(raw, @"<[^>]*>", "");
            code = WebUtility.HtmlDecode(code).Trim('\n', '\r').TrimEnd();
            if (code.Length >= minLength && seen.Add(code))
                blocks.Add(code);
        }

        foreach (Match m in Regex.Matches(html, @"<pre[^>]*>([\s\S]*?)</pre>", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);

        // Standalone multi-line <code> blocks outside <pre>
        string withoutPre = Regex.Replace(html, @"<pre[^>]*>[\s\S]*?</pre>", "", RegexOptions.IgnoreCase);
        foreach (Match m in Regex.Matches(withoutPre, @"<code[^>]*>([\s\S]*?)</code>", RegexOptions.IgnoreCase))
        {
            if (m.Groups[1].Value.Contains('\n'))
                Add(m.Groups[1].Value);
        }

        return blocks;
    }

    /// <summary>
    /// Fetches a page and returns its raw HTML (with timeout and size cap), or null on failure.
    /// </summary>
    internal static async Task<string?> FetchPageHtmlAsync(Uri url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(PageFetchTimeoutSeconds));
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            var buffer = new char[MaxPageBytes];
            int read = await reader.ReadBlockAsync(buffer, cts.Token);
            return new string(buffer, 0, read);
        }
        catch
        {
            return null;
        }
    }

    internal static string HtmlToPlainText(string html)
    {
        html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>|<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<header[^>]*>[\s\S]*?</header>|<footer[^>]*>[\s\S]*?</footer>|<nav[^>]*>[\s\S]*?</nav>", "", RegexOptions.IgnoreCase);

        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</(p|div|h1|h2|h3|h4|li|section|article)>", "\n", RegexOptions.IgnoreCase);

        string plainText = Regex.Replace(html, @"<[^>]*>", "");
        plainText = WebUtility.HtmlDecode(plainText);
        plainText = Regex.Replace(plainText, @"[ \t]+", " ");
        plainText = Regex.Replace(plainText, @"\r\n|\n|\r", "\n");
        plainText = Regex.Replace(plainText, @"\n{3,}", "\n\n").Trim();
        return plainText;
    }
}

public record SearchResultItem(string Title, string Url, string Snippet = "");
