using System.ComponentModel.DataAnnotations;

namespace TextChunker.Api.Services;

/// <summary>API settings (the <c>Chunking</c> section in appsettings/environment variables).</summary>
public sealed class ApiOptions
{
    /// <summary>Maximum input text length in characters (otherwise 413).</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Chunking:MaxTextChars must be greater than zero.")]
    public int MaxTextChars { get; set; } = 5_000_000;

    /// <summary>tokenizer.json cache directory; null — the default path.</summary>
    public string? TokenizerCacheDir { get; set; }
}
