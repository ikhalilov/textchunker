namespace TextChunker.Abstractions;

/// <summary>Сегмент чанка внутри входящего текста: (текст, start, end) — CharIndex в `text`.</summary>
public record ChunkSegment(string Text, int StartInText, int EndInText);

public interface IChunker
{
    /// <summary>
    /// Разрезать <paramref name="text"/> (фрагмент с учётом startChar/offset) на чанки:
    /// жадный алгоритм с перекрытием, разрезка по разделителям, финальный пересчёт токенов сервисом.
    /// <paramref name="final"/> = true — вход закончился, остаток нужно выдать последним чанком.
    /// <see cref="ChunkSplit.MoreNeeded"/> = true, если фрагмент закончился внутри потенциального чанка
    /// (нужно продолжить чтение, чтобы не выводить частичный жёсткий срез).
    /// </summary>
    ChunkSplit Chunk(string text, ITokenizer tokenizer, ChunkingOptions options, bool final = false);
}
