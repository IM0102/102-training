using OrderHub.Core.Domain;

namespace OrderHub.Core.Ai;

/// <summary>
/// 自然語言查訂單的白名單查詢參數:LLM 只能產生這組參數,
/// SQL 一律由 EF Core 從參數生成,模型碰不到查詢語句。
/// </summary>
public class OrderSearchQuery
{
    public OrderStatus? Status { get; set; }
    public CustomerTier? MemberTier { get; set; }

    /// <summary>起始日(含當日)。</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>結束日(含當日)。</summary>
    public DateTime? DateTo { get; set; }

    public bool HasAnyFilter =>
        Status.HasValue || MemberTier.HasValue || DateFrom.HasValue || DateTo.HasValue;
}
