namespace OrderHub.Infrastructure.Gemini;

public interface IGeminiJsonClient
{
    /// <summary>以 structured output 強制模型輸出符合 schema 的 JSON,回傳原始 JSON 字串。</summary>
    Task<string> GenerateJsonAsync(string input, string responseSchemaJson, CancellationToken cancellationToken = default);
}
