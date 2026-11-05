using System.ComponentModel.DataAnnotations;

namespace TextChunker.Api.Services;

/// <summary>Настройки API (секция <c>Chunking</c> в appsettings/переменных окружения).</summary>
public sealed class ApiOptions
{
    /// <summary>Максимальная длина входного текста в символах (иначе 413).</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Chunking:MaxTextChars must be greater than zero.")]
    public int MaxTextChars { get; set; } = 5_000_000;

    /// <summary>Каталог кэша tokenizer.json; null — путь по умолчанию.</summary>
    public string? TokenizerCacheDir { get; set; }
}
