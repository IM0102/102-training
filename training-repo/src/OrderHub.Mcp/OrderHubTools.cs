using ModelContextProtocol.Server;
using OrderHub.Core.Domain;
using OrderHub.Core.Interfaces;
using OrderHub.Core.Services;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;

[McpServerToolType]
public class OrderHubTools(IOrderService orderService, IProductRepository productRepository)
{
    // UnsafeRelaxedJsonEscaping 讓中文不被轉成 \uXXXX,方便閱讀與除錯
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    [McpServerTool, Description("依訂單編號查詢訂單,含客戶、品項、單價快照、會員折扣與應付總額")]
    public async Task<string> GetOrder([Description("訂單 Id")] int id)
    {
        var order = await orderService.GetOrderAsync(id);
        if (order is null)
            return $"找不到訂單 {id}";

        var result = new
        {
            order.Id,
            order.CreatedAt,
            Status = order.Status.ToString(),
            Customer = order.Customer is null ? null : new
            {
                order.Customer.Id,
                order.Customer.Name,
                Tier = order.Customer.Tier.ToString()
            },
            Items = order.Items.Select(i => new
            {
                i.ProductId,
                i.Product?.Sku,
                i.Product?.Name,
                i.Quantity,
                i.UnitPriceSnapshot,
                LineTotal = i.UnitPriceSnapshot * i.Quantity
            }),
            Subtotal = orderService.CalculateSubtotal(order),
            DiscountRate = orderService.GetDiscountRate(order.Customer?.Tier ?? CustomerTier.Standard),
            Total = orderService.CalculateTotal(order)
        };
        return JsonSerializer.Serialize(result, Json);
    }

    [McpServerTool, Description("列出庫存低於門檻且仍在販售的商品,依庫存量升冪排序")]
    public async Task<string> LowStock([Description("庫存門檻,預設 10")] int threshold = 10)
    {
        var products = await productRepository.GetActiveAsync();
        var items = products
            .Where(p => p.StockQuantity < threshold)
            .OrderBy(p => p.StockQuantity)
            .Select(p => new { p.Sku, p.Name, p.StockQuantity })
            .ToList();
        return JsonSerializer.Serialize(items, Json);
    }

    [McpServerTool, Description("查詢某位客戶的全部訂單摘要(編號、日期、狀態、應付總額)")]
    public async Task<string> CustomerOrders([Description("客戶 Id")] int customerId)
    {
        var orders = await orderService.GetCustomerOrdersAsync(customerId);
        var result = orders.Select(o => new
        {
            o.Id,
            o.CreatedAt,
            Status = o.Status.ToString(),
            Total = orderService.CalculateTotal(o)
        });
        return JsonSerializer.Serialize(result, Json);
    }
}
