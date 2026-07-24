using OrderHub.Core.Domain;

namespace OrderHub.Core.Services;

internal static class OrderCreationValidator
{
    public static string? ValidateRequest(Customer? customer, IReadOnlyList<NewOrderLine>? lines)
    {
        if (customer is null)
            return "找不到指定的客戶";

        if (lines is null || lines.Count == 0)
            return "訂單至少需要一項商品";

        if (lines.Any(l => l.Quantity <= 0))
            return "商品數量必須大於 0";

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            return "同一商品請勿重複加入，請調整數量即可";

        return null;
    }

    public static string? ValidateLine(Product? product, NewOrderLine line)
    {
        if (product is null || !product.IsActive)
            return $"商品（Id={line.ProductId}）不存在或已停售";

        if (product.StockQuantity < line.Quantity)
            return $"商品「{product.Name}」庫存不足（現有 {product.StockQuantity}，需求 {line.Quantity}）";

        return null;
    }
}
