namespace TextChunker.Abstractions;

/// <summary>Pluggable tokenizer. The primary exact mode is the model tokenizer (HF tokenizers + tokenizer.json).</summary>
public interface ITokenizer
{
    /// <summary>Tokenizer name (`char`, `word`, `regex`, `huggingface`).</summary>
    string Name { get; }

    /// <summary>Model name/identifier (for the model tokenizer).</summary>
    string? ModelName { get; }

    /// <summary>Number of tokens in the text.</summary>
    int CountTokens(string text);

    /// <summary>
    /// Hard cut at a token boundary: returns the CharIndex where token number <paramref name="n"/> begins
    /// (0-based); if n == CountTokens(text), returns the text length.
    /// In Char/Word modes — CharIndex by runes/words.
    /// </summary>
    int? CutAtTokenBoundary(string text, int n);
}
