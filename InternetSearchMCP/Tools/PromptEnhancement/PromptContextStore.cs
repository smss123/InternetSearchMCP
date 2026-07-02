using System.Text.RegularExpressions;

namespace InternetSearchMCP.Tools.PromptEnhancement;

/// <summary>
/// Bounded in-memory session history used to expand vague follow-up prompts.
/// One stdio server process serves one client, so a static store is sufficient.
/// </summary>
internal static class PromptContextStore
{
    private const int Cap = 10;
    private static readonly Queue<(string Prompt, List<string> Topics)> _history = new();
    private static readonly object _lock = new();

    internal static void Add(string prompt)
    {
        lock (_lock)
        {
            _history.Enqueue((prompt, ExtractTopics(prompt)));
            while (_history.Count > Cap) _history.Dequeue();
        }
    }

    /// <summary>Topics from the most recent prompts, newest first.</summary>
    internal static List<string> RecentTopics(int maxTopics = 6)
    {
        lock (_lock)
        {
            return _history.Reverse()
                .SelectMany(h => h.Topics)
                .Distinct()
                .Take(maxTopics)
                .ToList();
        }
    }

    /// <summary>
    /// Topic extraction: code-like identifiers (CamelCase/dotted) plus the longest
    /// words of the prompt — script-agnostic, no language assumptions.
    /// </summary>
    private static List<string> ExtractTopics(string prompt)
    {
        var identifiers = Regex.Matches(prompt, @"\b[A-Za-z_][A-Za-z0-9_]*(?:[A-Z][a-z0-9]|\.[A-Za-z_])[A-Za-z0-9_.]*\b")
            .Select(m => m.Value);

        var longWords = Regex.Matches(prompt, @"[\p{L}\p{N}]{5,}")
            .Select(m => m.Value)
            .OrderByDescending(w => w.Length)
            .Take(4);

        return identifiers.Concat(longWords).Distinct().Take(8).ToList();
    }
}
