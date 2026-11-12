namespace TextChunker.Chunking.Semantic;

/// <summary>Atom kind — a text unit that semantic chunking treats as indivisible.</summary>
public enum SemanticAtomKind
{
    /// <summary>A sentence or part of one.</summary>
    Sentence,

    /// <summary>A Markdown heading (<c># …</c>).</summary>
    Heading,

    /// <summary>A list item (<c>- </c>, <c>* </c>, <c>1. </c>).</summary>
    ListItem,

    /// <summary>A fenced code block (<c>```</c> / <c>~~~</c>), never split.</summary>
    Code,

    /// <summary>Other text (for example, a line with no structural markers).</summary>
    Text
}

/// <summary>
/// A semantic chunking atom: <see cref="Text"/> is the trimmed content,
/// <see cref="Start"/>/<see cref="End"/> are its boundaries in the source text
/// (<see cref="End"/> is exclusive, UTF-16 indices). Atoms are ordered and do not overlap;
/// the gaps between them are separators (whitespace/line breaks).
/// </summary>
public sealed record SemanticAtom(string Text, int Start, int End, SemanticAtomKind Kind)
{
    public int Length => End - Start;
}
