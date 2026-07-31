using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerResourceType]
public class OrderHubResources
{
    [McpServerResource(UriTemplate = "orderhub://discount-rules",
        Name = "會員折扣規則", MimeType = "text/markdown")]
    [Description("目前生效的會員折扣規則與計算方式")]
    // 折數與 OrderService.GetDiscountRate 是兩份真相,改折扣時兩邊都要動
    public static string DiscountRules() => """
        # OrderHub 會員折扣規則
        - Standard:不打折
        - Silver:95 折
        - Gold:9 折
        折扣在訂單總額上折抵一次,單價快照(UnitPriceSnapshot)為下單當下原價。
        """;
}
