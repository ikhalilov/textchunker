namespace TextChunker.Abstractions;

/// <summary>Подключаемый токенизатор. Основной точный режим — модельный (HF tokenizers + tokenizer.json).</summary>
public interface ITokenizer
{
    /// <summary>Имя токенизатора (`char`, `word`, `regex`, `huggingface`).</summary>
    string Name { get; }

    /// <summary>Имя/идентификатор модели (для модельного токенизатора).</summary>
    string? ModelName { get; }

    /// <summary>Количество токенов текста.</summary>
    int CountTokens(string text);

    /// <summary>
    /// Жёсткий рез на границе токена: вернёт CharIndex, с которого начинается токен номер <paramref name="n"/>
    /// (0-based); если n == CountTokens(text) — возвращает длину текста.
    /// В Char/Word-режимах — CharIndex по rune/словам.
    /// </summary>
    int? CutAtTokenBoundary(string text, int n);
}
