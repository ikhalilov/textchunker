namespace TextChunker.Abstractions;

/// <summary>
/// Финальный пересчёт токенов собранного чанка (task 3.4): count считается по итоговому
/// тексту <c>prefix + chunkText</c>, а не как сумма токенов частей — на границе склейки
/// число токенов модели может меняться (например, BPE <c>"hello wor" + "ld"</c>).
/// </summary>
public static class ChunkTokenCount
{
    /// <summary>
    /// Точное число токенов итогового текста чанка с учётом task-префикса.
    /// Значение записывается в <see cref="Chunk.TokenCount"/>.
    /// </summary>
    public static int ForFinalChunk(ITokenizer tokenizer, string chunkText, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(chunkText);

        string combined = string.IsNullOrEmpty(prefix) ? chunkText : prefix + chunkText;
        return tokenizer.CountTokens(combined);
    }
}
