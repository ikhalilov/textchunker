namespace TextChunker.Abstractions;

/// <summary>
/// Final token recount for an assembled chunk (task 3.4): the count is computed over the resulting
/// text <c>prefix + chunkText</c>, not as the sum of the token counts of the parts, because the
/// model's token count can change at the join boundary (for example, BPE <c>"hello wor" + "ld"</c>).
/// </summary>
public static class ChunkTokenCount
{
    /// <summary>
    /// Exact token count of the chunk's final text, including the task prefix.
    /// The value is written to <see cref="Chunk.TokenCount"/>.
    /// </summary>
    public static int ForFinalChunk(ITokenizer tokenizer, string chunkText, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(chunkText);

        string combined = string.IsNullOrEmpty(prefix) ? chunkText : prefix + chunkText;
        return tokenizer.CountTokens(combined);
    }
}
