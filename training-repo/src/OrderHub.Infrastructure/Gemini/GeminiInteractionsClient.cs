using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderHub.Core.Ai;

namespace OrderHub.Infrastructure.Gemini;

/// <summary>
/// 裸 HttpClient 呼叫 Gemini Interactions API(POST /v1/interactions)。
/// 免費層一定會撞 429:重試時優先尊重回應附帶的建議等待時間,再退而用指數退避;
/// 重試耗盡擲 AiServiceUnavailableException,讓 Web 層回 503 而不是 500。
/// </summary>
public class GeminiInteractionsClient : IGeminiJsonClient
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiInteractionsClient> _logger;

    public GeminiInteractionsClient(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiInteractionsClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GenerateJsonAsync(string input, string responseSchemaJson, CancellationToken cancellationToken = default)
    {
        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AiServiceUnavailableException("Gemini API key 未設定:user-secrets 的 Gemini:ApiKey 或環境變數 GEMINI_API_KEY");

        using var schema = JsonDocument.Parse(responseSchemaJson);
        var body = JsonSerializer.Serialize(new
        {
            model = _options.Model,
            input,
            response_format = new { type = "text", mime_type = "application/json", schema = schema.RootElement }
        });

        TimeSpan? delay = null;
        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            if (delay is not null)
            {
                _logger.LogWarning("Gemini 暫時失敗,{Seconds:0.#} 秒後重試(第 {Attempt}/{Max} 次)",
                    delay.Value.TotalSeconds, attempt, _options.MaxRetries);
                await Task.Delay(delay.Value, cancellationToken);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException)
            {
                delay = ExponentialBackoff(attempt);   // 網路層錯誤,退避後重試
                continue;
            }

            using (response)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                    return ExtractModelOutput(payload);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new AiServiceUnavailableException("Gemini 拒絕存取:API key 無效或專案權限不足");

                // 429 / 5xx:可重試。429 優先尊重 error details 的建議等待時間
                delay = response.StatusCode == HttpStatusCode.TooManyRequests
                    ? SuggestedRetryDelay(payload) ?? ExponentialBackoff(attempt)
                    : ExponentialBackoff(attempt);
            }
        }

        throw new AiServiceUnavailableException($"Gemini 重試 {_options.MaxRetries} 次後仍失敗,請稍後再試");
    }

    private static TimeSpan ExponentialBackoff(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt));

    /// <summary>429 的 error details 會附 RetryInfo(例如 "retryDelay": "17s")。</summary>
    private static TimeSpan? SuggestedRetryDelay(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("retryDelay", out var retryDelay) &&
                        retryDelay.GetString() is { } text &&
                        text.EndsWith("s") &&
                        double.TryParse(text.TrimEnd('s'), out var seconds))
                    {
                        return TimeSpan.FromSeconds(seconds);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // 回應不是 JSON 就走指數退避
        }
        return null;
    }

    /// <summary>從 Interactions 回應撈出 model_output 步驟的 JSON 文字。</summary>
    private static string ExtractModelOutput(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.TryGetProperty("steps", out var steps))
        {
            foreach (var step in steps.EnumerateArray())
            {
                if (step.TryGetProperty("type", out var type) && type.GetString() == "model_output" &&
                    step.TryGetProperty("content", out var content))
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } json)
                            return json;
                    }
                }
            }
        }
        throw new AiServiceUnavailableException("Gemini 回應中沒有 model_output,無法取得結果");
    }
}
