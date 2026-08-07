namespace OrderHub.Core.Ai;

/// <summary>
/// AI 服務暫時不可用(rate limit 重試耗盡、金鑰未設定、上游錯誤)。
/// 呼叫端應轉成 503 之類的明確回應,而不是讓它變成 500。
/// </summary>
public class AiServiceUnavailableException : Exception
{
    public AiServiceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
