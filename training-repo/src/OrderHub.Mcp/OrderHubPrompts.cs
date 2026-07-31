using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerPromptType]
public class OrderHubPrompts
{
    [McpServerPrompt(Name = "low_stock_report"), Description("產生低庫存採購建議報告")]
    public static ChatMessage LowStockReport(
        [Description("庫存門檻,預設 10")] int threshold = 10) =>
        new(ChatRole.User, $"""
            請用 low_stock 工具(threshold={threshold})查出低庫存商品,
            再用其他工具了解這些商品的近期訂單狀況,
            最後輸出採購建議表:SKU、名稱、現有庫存、建議補貨量、理由。
            """);
}
