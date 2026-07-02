namespace InternetSearchMCP.Tools.PromptEnhancement;

/// <summary>
/// Tracks whether enhance_prompt has been called recently, so other tools can
/// nudge clients that skip the enhancement step. MCP cannot enforce call order;
/// this is a soft server-side reminder.
/// </summary>
internal static class EnhancementTracker
{
    private static int _callsSinceEnhance = int.MaxValue;
    private static readonly object _lock = new();

    internal static void MarkEnhanced()
    {
        lock (_lock) _callsSinceEnhance = 0;
    }

    /// <summary>
    /// Registers a non-enhancement tool call and returns a reminder string when
    /// the client appears to be skipping enhance_prompt (never called, or more
    /// than 3 tool calls ago), otherwise an empty string.
    /// </summary>
    internal static string NudgeIfSkipped()
    {
        lock (_lock)
        {
            bool stale = _callsSinceEnhance > 3;
            if (_callsSinceEnhance != int.MaxValue) _callsSinceEnhance++;
            return stale
                ? "REMINDER: You did not call enhance_prompt on the user's latest raw message. For the NEXT user message, ALWAYS call enhance_prompt first — it improves your answer and picks the right tool.\n\n"
                : "";
        }
    }
}
