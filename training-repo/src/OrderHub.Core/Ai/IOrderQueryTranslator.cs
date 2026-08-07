namespace OrderHub.Core.Ai;

public interface IOrderQueryTranslator
{
    /// <summary>
    /// 將自然語言查詢轉成白名單參數。回傳 null 表示無法理解、參數值不在白名單內,
    /// 或使用者的意圖不是「查詢訂單」(例如要求刪除資料)。
    /// </summary>
    Task<OrderSearchQuery?> TranslateAsync(string naturalLanguageQuery, CancellationToken cancellationToken = default);
}
