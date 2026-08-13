namespace AutoCorrect.Core.Engines.Llm;

/// <summary>
/// Every prompt the language model sees, as constants in one place.
///
/// The system prompt is short and identical for every call on purpose: the server can then
/// reuse its KV cache across requests, so the prefill is practically free and the first token
/// arrives much sooner.
/// </summary>
public static class Prompts
{
    /// <summary>Never changes between calls. Keep it that way.</summary>
    public const string System =
        "Du überarbeitest Text. Gib ausschliesslich den überarbeiteten Text aus. " +
        "Keine Erklärung, keine Anführungszeichen, keine Einleitung, kein Kommentar. " +
        "Antworte in der Sprache des Eingabetexts. " +
        "Im Deutschen gilt Schweizer Rechtschreibung: ss statt ß.";

    public const string Rephrase =
        "Formuliere den folgenden Text um. Gleicher Inhalt, natürlichere und klarere Formulierung.";

    public const string Formal =
        "Formuliere den folgenden Text förmlicher und höflicher. Gleicher Inhalt.";

    public const string Shorten =
        "Kürze den folgenden Text deutlich, ohne Inhalt zu verlieren.";

    /// <summary>Sequences that end generation; they are artefacts of chat templates.</summary>
    public static readonly string[] Stop = ["<|im_end|>", "</s>", "<|endoftext|>"];

    public static string Instruction(ProcessingMode mode) => mode switch
    {
        ProcessingMode.Rephrase => Rephrase,
        ProcessingMode.Formal => Formal,
        ProcessingMode.Shorten => Shorten,
        _ => throw new NotSupportedException($"No prompt for mode {mode}."),
    };

    /// <summary>The user turn: instruction and text, separated so the model cannot confuse them.</summary>
    public static string User(ProcessingMode mode, string text) =>
        $"{Instruction(mode)}\n\n---\n{text}\n---";
}
