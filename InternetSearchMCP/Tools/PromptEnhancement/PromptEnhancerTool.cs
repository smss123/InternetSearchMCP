using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace InternetSearchMCP.Tools.PromptEnhancement;

public class PromptEnhancerTool
{
    private enum PromptType { CodeError, CodeHowTo, FactualQuestion, WritingTask, Vague }

    // Weak secondary signals only — classification leans on language-neutral patterns.
    private static readonly string[] QuestionWords =
        ["what", "when", "who", "where", "why", "how", "which",
         "ما", "ماذا", "متى", "من", "أين", "لماذا", "كيف", "كم",
         "was", "wann", "wer", "que", "quand", "qui", "что", "когда", "кто", "什么", "何"];

    private static readonly string[] WritingWords =
        ["write", "summarize", "summarise", "translate", "rewrite", "draft", "compose",
         "اكتب", "لخص", "ترجم", "صغ",
         "écris", "résume", "traduis", "schreibe", "escribe", "resume", "напиши", "переведи", "写", "翻译"];

    private static readonly string[] Pronouns =
        ["it", "this", "that", "them", "these", "those",
         "هذا", "ذلك", "هذه", "ها",
         "это", "оно", "das", "ça", "cela", "esto", "eso", "它", "这"];

    private static readonly string[] HowToWords =
        ["how", "implement", "create", "build", "add", "configure", "setup", "fix",
         "كيف", "طريقة", "اصلاح", "أنشئ", "اضف",
         "comment", "wie", "cómo", "как", "怎么"];

    [McpServerTool]
    [Description("ALWAYS call this tool FIRST with the user's raw prompt, before answering or calling any other tool. It rewrites the prompt into a structured, context-enriched version and recommends which tool to use next. Adopt the returned ENHANCED PROMPT as the working prompt. Works for any language.")]
    public static Task<string> EnhancePromptAsync(
        [Description("The user's raw, unmodified prompt text (any language).")] string rawPrompt)
    {
        if (string.IsNullOrWhiteSpace(rawPrompt))
            return Task.FromResult("ERROR: Raw prompt cannot be empty.");

        EnhancementTracker.MarkEnhanced();
        rawPrompt = rawPrompt.Trim();
        var type = Classify(rawPrompt);

        List<string> contextTopics = [];
        if (type == PromptType.Vague)
            contextTopics = PromptContextStore.RecentTopics();

        PromptContextStore.Add(rawPrompt);

        var keyElements = ExtractKeyElements(rawPrompt);
        string subject = BuildSubject(rawPrompt, keyElements);

        var sb = new StringBuilder();
        sb.AppendLine("UNDERSTANDING:");
        sb.AppendLine(BuildUnderstanding(rawPrompt, type, subject, keyElements, contextTopics));
        sb.AppendLine();
        sb.AppendLine("ENHANCED PROMPT:");
        sb.AppendLine(BuildEnhancedPrompt(rawPrompt, type, subject, keyElements, contextTopics));
        sb.AppendLine();
        string recommendation = type switch
        {
            PromptType.CodeError or PromptType.CodeHowTo =>
                $"xprema_code (suggested problem argument: \"{BuildSearchQuery(rawPrompt)}\")",
            PromptType.FactualQuestion =>
                $"smart_search (suggested query argument: \"{BuildSearchQuery(rawPrompt)}\")",
            _ => "none"
        };
        sb.AppendLine($"RECOMMENDED NEXT TOOL: {recommendation}");
        if (contextTopics.Count > 0)
            sb.AppendLine($"CONTEXT USED: expanded vague prompt with recent session topics: {string.Join(", ", contextTopics)}");

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    private static PromptType Classify(string prompt)
    {
        // Language-neutral signals first: error text is English-ish in every locale.
        bool hasErrorPattern =
            Regex.IsMatch(prompt, @"\b\w+(Exception|Error)\b") ||
            Regex.IsMatch(prompt, @"\berror\s+\w*\d+", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(prompt, @"\b(Traceback|stack ?trace|errno|segfault|NPE)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(prompt, @"^\s+at\s+[\w.]+\(", RegexOptions.Multiline);

        bool hasCodeSignals =
            prompt.Contains('`') ||
            Regex.IsMatch(prompt, @"[{};]\s*$", RegexOptions.Multiline) ||
            Regex.IsMatch(prompt, @"\b[a-z]+[A-Z]\w*\b") ||          // camelCase
            Regex.IsMatch(prompt, @"\b\w+\.\w+\(") ||                 // method calls
            Regex.IsMatch(prompt, @"\b(c#|csharp|python|javascript|typescript|java|sql|css|html|json|api|sdk|npm|nuget|dotnet|\.net)\b", RegexOptions.IgnoreCase);

        if (hasErrorPattern) return PromptType.CodeError;

        var words = Tokenize(prompt);

        if (hasCodeSignals && ContainsAny(words, HowToWords)) return PromptType.CodeHowTo;
        if (hasCodeSignals) return PromptType.CodeHowTo;

        // Question marks are near-universal across scripts.
        bool looksLikeQuestion = prompt.Contains('?') || prompt.Contains('؟') || prompt.Contains('？')
                                 || ContainsAny(words, QuestionWords);

        // Vague signals: pronoun-heavy with no concrete nouns/identifiers, or very short non-questions.
        bool noConcreteNouns = !words.Any(w => w.Length >= 5);
        bool pronounHeavy = ContainsAny(words, Pronouns);
        if (pronounHeavy && noConcreteNouns) return PromptType.Vague;
        if (words.Count < 4 && !looksLikeQuestion) return PromptType.Vague;

        if (ContainsAny(words, WritingWords)) return PromptType.WritingTask;
        if (looksLikeQuestion) return PromptType.FactualQuestion;
        if (words.Count < 6) return PromptType.Vague;

        return PromptType.Vague;
    }

    /// <summary>
    /// Mirror principle: the user's wording is quoted verbatim — never translated,
    /// never paraphrased — so enhancement works identically for any language.
    /// Templates apply full prompt-engineering structure: role, task, key elements,
    /// reasoning approach, output contract, and quality bar.
    /// </summary>
    /// <summary>
    /// Phase 1 of the prompt-writing standard: state what was understood from the
    /// raw prompt BEFORE generating anything, so the generated prompt is traceable.
    /// </summary>
    private static string BuildUnderstanding(string rawPrompt, PromptType type, string subject,
        List<string> keyElements, List<string> contextTopics)
    {
        var sb = new StringBuilder();

        string intent = type switch
        {
            PromptType.CodeError => "The user has a failing piece of code and wants it diagnosed and fixed.",
            PromptType.CodeHowTo => "The user wants working code that implements something specific.",
            PromptType.FactualQuestion => "The user is asking a question and expects an accurate, sourced answer.",
            PromptType.WritingTask => "The user wants a piece of text produced for them.",
            _ => "The user's request is underspecified; intent must be inferred from context."
        };
        sb.AppendLine($"- Intent: {intent}");
        if (!string.IsNullOrWhiteSpace(subject))
            sb.AppendLine($"- Subject: {subject}");
        if (keyElements.Count > 0)
            sb.AppendLine($"- Concrete signals: {string.Join("; ", keyElements)}");
        if (contextTopics.Count > 0)
            sb.AppendLine($"- Inferred from session context: {string.Join(", ", contextTopics)}");

        string missing = type switch
        {
            PromptType.CodeError when !Regex.IsMatch(rawPrompt, @"```|[{};]") =>
                "- Missing: the surrounding code was not provided; the answer should state assumptions or request the snippet.",
            PromptType.WritingTask => "- Missing (if not stated): audience, length, and tone — infer sensible defaults.",
            PromptType.Vague => "- Missing: an explicit subject; relying on session context above.",
            _ => ""
        };
        if (missing.Length > 0) sb.AppendLine(missing);

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Condensed subject line: key-element values first, else top content words.
    /// </summary>
    private static string BuildSubject(string rawPrompt, List<string> keyElements)
    {
        if (keyElements.Count > 0)
            return string.Join(", ", keyElements.Select(e => e[(e.IndexOf(' ') + 1)..]).Distinct().Take(4));
        return BuildSearchQuery(rawPrompt);
    }

    private static string BuildEnhancedPrompt(string rawPrompt, PromptType type, string subject,
        List<string> keyElements, List<string> contextTopics)
    {
        var sb = new StringBuilder();

        string role = type switch
        {
            PromptType.CodeError => "You are a senior software engineer doing root-cause debugging.",
            PromptType.CodeHowTo => "You are a senior software engineer known for minimal, production-ready implementations.",
            PromptType.FactualQuestion => "You are a meticulous researcher who only states what sources support.",
            PromptType.WritingTask => "You are a professional writer who nails tone and audience on the first draft.",
            _ => "You are a careful assistant who resolves ambiguity before acting."
        };
        sb.AppendLine(role);
        sb.AppendLine();

        // The subject from the understanding phase is woven into the task statement
        // so the generated prompt stands on its own.
        string subjectClause = string.IsNullOrWhiteSpace(subject) ? "" : $" concerning: {subject}";
        string task = type switch
        {
            PromptType.CodeError => $"Task: Diagnose the error{subjectClause}. Find the ROOT CAUSE (not just the symptom), then provide the corrected code and explain why the fix is correct.",
            PromptType.CodeHowTo => $"Task: Provide a complete, working implementation{subjectClause} — runnable as-is, following the conventions of the language/framework involved.",
            PromptType.FactualQuestion => $"Task: Answer the question{subjectClause} accurately using current, verifiable information. Distinguish established facts from claims; never guess.",
            PromptType.WritingTask => $"Task: Produce the requested text{subjectClause}, matching the implied tone, audience, and format. Deliver the text itself — no meta-commentary.",
            _ => $"Task: Interpret the request{subjectClause}. If a reasonable interpretation exists, fulfill it; state the interpretation you chose in one line."
        };
        sb.AppendLine(task);
        sb.AppendLine();
        sb.AppendLine("Original prompt (verbatim):");
        sb.AppendLine($"\"{rawPrompt}\"");

        if (keyElements.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Key elements detected (address each explicitly): {string.Join("; ", keyElements)}");
        }

        if (contextTopics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"This likely refers to the recent conversation topics: {string.Join(", ", contextTopics)}. Confirm the reference fits before relying on it.");
        }

        sb.AppendLine();
        string approach = type switch
        {
            PromptType.CodeError => "Approach: (1) read the error literally, (2) identify which code path produces it, (3) list candidate causes and eliminate them against the evidence, (4) fix the surviving cause.",
            PromptType.CodeHowTo => "Approach: choose the idiomatic solution first; note simpler/alternative options only if they change a real trade-off.",
            PromptType.FactualQuestion => "Approach: verify against at least one source before asserting; flag anything uncertain or time-sensitive with its date.",
            PromptType.WritingTask => "Approach: infer audience and purpose from the request; prefer concrete wording over filler.",
            _ => "Approach: pick the most probable intent; do not ask clarifying questions unless genuinely blocked."
        };
        sb.AppendLine(approach);
        sb.AppendLine();

        string format = type switch
        {
            PromptType.CodeError => "Expected output: (1) root cause in 1-2 sentences, (2) corrected code in fenced blocks, (3) why the fix works, (4) how to prevent recurrence (one line).",
            PromptType.CodeHowTo => "Expected output: brief plan (2-3 bullets), then complete code in fenced blocks, then usage example and caveats.",
            PromptType.FactualQuestion => "Expected output: direct answer in the first sentence, then supporting details, then source URLs.",
            PromptType.WritingTask => "Expected output: the requested text only, ready to use without edits.",
            _ => "Expected output: a direct, structured response; lead with the answer."
        };
        sb.AppendLine(format);
        sb.AppendLine("Quality bar: correct over fast; specific over generic; if information is missing, say exactly what is missing instead of inventing it.");
        sb.AppendLine();

        string blindSpotHint = type switch
        {
            PromptType.CodeError => "e.g. a deeper design flaw behind the error, version incompatibilities, or the same bug lurking elsewhere in their code",
            PromptType.CodeHowTo => "e.g. security/performance pitfalls of the requested approach, a simpler standard solution they may not know exists, or maintenance costs",
            PromptType.FactualQuestion => "e.g. common misconceptions around this topic, important context that changes the answer, or a better question they should be asking",
            PromptType.WritingTask => "e.g. audience or cultural considerations they may have missed, stronger formats for the same goal, or claims that need verification",
            _ => "e.g. what their request is probably really aiming at, and nearby options they may not know exist"
        };
        sb.AppendLine($"Blind-spot rule: while answering, actively look for DARK POINTS — relevant issues the user likely does not know to ask about ({blindSpotHint}). " +
                      "End your response with a short \"Suggestions\" section: (1) assumptions you made, (2) the dark points found, (3) 2-3 concrete next steps or better questions. " +
                      "Skip this section only if there is genuinely nothing the user is missing.");
        sb.Append("Respond in the same language as the original prompt above.");

        return sb.ToString();
    }

    /// <summary>
    /// Language-neutral extraction of concrete anchors the answer must address:
    /// exception/error names, code identifiers, quoted strings, URLs, file paths, numbers with units.
    /// </summary>
    private static List<string> ExtractKeyElements(string prompt)
    {
        var elements = new List<string>();

        void AddAll(string pattern, string label) =>
            elements.AddRange(Regex.Matches(prompt, pattern).Select(m => $"{label} {m.Value}").Distinct().Take(3));

        AddAll(@"\b\w+(Exception|Error)\b", "error:");
        AddAll(@"\berror\s+\w*\d+\b", "error code:");
        AddAll(@"\bhttps?://\S+", "URL:");
        AddAll(@"(?:[A-Za-z]:\\|/)[\w./\\-]+\.\w{1,5}\b", "file:");
        AddAll(@"\b[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*\(?", "identifier:");
        AddAll(@"""[^""\n]{3,60}""|'[^'\n]{3,60}'", "quoted:");

        return elements.Distinct().Take(8).ToList();
    }

    /// <summary>
    /// Condenses the raw prompt into a search-ready query: error names and identifiers
    /// first, then the longest content words — quotes and filler stripped.
    /// </summary>
    private static string BuildSearchQuery(string prompt)
    {
        var anchors = Regex.Matches(prompt, @"\b\w+(Exception|Error)\b|\b[A-Za-z_]\w*\.[A-Za-z_]\w*\b")
            .Select(m => m.Value).Distinct().Take(3).ToList();

        var contentWords = Regex.Matches(prompt, @"[\p{L}\p{N}#+.]{4,}")
            .Select(m => m.Value)
            .Where(w => !anchors.Any(a => a.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Distinct().Take(6);

        string query = string.Join(" ", anchors.Concat(contentWords));
        return query.Length > 120 ? query[..120] : query;
    }

    private static List<string> Tokenize(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).ToList();

    private static bool ContainsAny(List<string> words, string[] set) => words.Any(set.Contains);
}
