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

        rawPrompt = rawPrompt.Trim();
        var type = Classify(rawPrompt);

        List<string> contextTopics = [];
        if (type == PromptType.Vague)
            contextTopics = PromptContextStore.RecentTopics();

        PromptContextStore.Add(rawPrompt);

        var sb = new StringBuilder();
        sb.AppendLine("ENHANCED PROMPT:");
        sb.AppendLine(BuildEnhancedPrompt(rawPrompt, type, contextTopics));
        sb.AppendLine();
        sb.AppendLine($"RECOMMENDED NEXT TOOL: {type switch
        {
            PromptType.CodeError or PromptType.CodeHowTo => "xprema_code",
            PromptType.FactualQuestion => "smart_search",
            _ => "none"
        }}");
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
    /// </summary>
    private static string BuildEnhancedPrompt(string rawPrompt, PromptType type, List<string> contextTopics)
    {
        var sb = new StringBuilder();

        string task = type switch
        {
            PromptType.CodeError => "Task: Diagnose and fix the following coding error. Identify the root cause, then provide corrected code.",
            PromptType.CodeHowTo => "Task: Provide a working implementation for the following coding request, with brief usage notes.",
            PromptType.FactualQuestion => "Task: Answer the following question accurately using current information; cite sources.",
            PromptType.WritingTask => "Task: Complete the following writing request. Match the requested tone and format; ask nothing, produce the text.",
            _ => "Task: Interpret and fulfill the following request."
        };
        sb.AppendLine(task);
        sb.AppendLine();
        sb.AppendLine("Original prompt (verbatim):");
        sb.AppendLine($"\"{rawPrompt}\"");

        if (contextTopics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"This likely refers to the recent conversation topics: {string.Join(", ", contextTopics)}.");
        }

        sb.AppendLine();
        string format = type switch
        {
            PromptType.CodeError or PromptType.CodeHowTo => "Expected output: root cause (1-2 sentences if applicable), then code in fenced blocks, then any caveats.",
            PromptType.FactualQuestion => "Expected output: direct answer first, then supporting details with source URLs.",
            PromptType.WritingTask => "Expected output: the requested text only, ready to use.",
            _ => "Expected output: a direct, structured response."
        };
        sb.AppendLine(format);
        sb.Append("Respond in the same language as the original prompt above.");

        return sb.ToString();
    }

    private static List<string> Tokenize(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).ToList();

    private static bool ContainsAny(List<string> words, string[] set) => words.Any(set.Contains);
}
