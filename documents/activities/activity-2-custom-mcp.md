# 活動 2 — 自建 MCP Server:給 agent 造工具

## 先搞懂:什麼是 MCP?

**MCP(Model Context Protocol)** 是 Anthropic 在 2024 年底發佈的開放協定,定義「AI agent 如何取得外部工具與資料」的標準介面。可以把它想成 **AI 世界的 USB-C**:server 端(你)把能力包成標準格式,client 端(Claude Code、Codex 等任何支援 MCP 的 agent)插上就能用——不用為每個 agent 各寫一套整合。

一個 MCP server 可以對外提供三種原語:**Tools**(agent 可呼叫的動作,例如查訂單)、**Resources**(唯讀的背景資料)、**Prompts**(預先定義的提示範本)——本活動的練習 1~5 會依序做到這三種。

### MCP 和 API 差在哪?

MCP 不是要取代 API——事實上 MCP server 內部通常就是在呼叫既有的 API 或程式庫(本活動就是把 OrderHub 的 service 層包給 agent 用)。差別在於**為誰設計**:

| 面向               | 傳統 API(REST / gRPC…)                    | MCP                                                           |
| ------------------ | ----------------------------------------- | ------------------------------------------------------------- |
| 使用者             | 人類開發者寫的程式                        | AI agent(LLM)                                                 |
| 如何知道怎麼用     | 開發者讀文件、寫呼叫程式碼                | agent 啟動時自動探索工具清單與 `Description`,自行決定何時呼叫 |
| 介面定義           | 每家 API 各自設計(路徑、參數、認證都不同) | 統一協定:所有 server 都用同一套格式描述工具、參數、回傳       |
| 整合成本           | 每接一個服務就寫一次整合程式              | server 寫一次,任何支援 MCP 的 client 都能直接接               |
| 傳輸方式           | HTTP 為主                                 | stdio(本機子行程,本活動採用)或 HTTP(遠端)                     |
| 「說明文字」的角色 | 文件給人看,寫爛了頂多開發者罵             | `Description` 直接決定 agent 會不會用、用得對不對——它就是 UX  |

一句話總結:**API 是給程式呼叫的介面,MCP 是給 agent 理解並自主使用的介面**。你在本活動最花心思的不會是程式邏輯(那些都在 service 層現成的),而是「怎麼描述工具,讓 agent 用得好」。

---

## 練習 0 — 先當使用者:接一個現成的 MCP

**目標**:先體驗「agent 有了新工具」是什麼感覺,再動手造自己的。

接上 Playwright MCP(瀏覽器自動化):

Terminal (專案根目錄執行):

```powershell
claude mcp add playwright -- npx @playwright/mcp@latest
```

Codex(`~/.codex/config.toml` 加入):

```toml
[mcp_servers.playwright]
command = "npx"
args = ["@playwright/mcp@latest"]
```

**任務**: 網站跑起來後,請 agent「建立一筆新訂單,截圖給我看結果頁」。

**驗證方式**:

- [ ] agent 能自己開瀏覽器完成操作並回傳截圖
- [ ] 回想活動 1 練習 2:當時人工重現 bug 的步驟,現在 agent 可以自己做——把這個對比記進 PROCESS.md

---

## 練習 1 — 建立 OrderHub MCP Server(stdio)

**目標**:一個 C# console 專案,透過 stdio 對外提供 3 個唯讀工具。

### 1a. 建專案

在 `training-repo` 下:

```powershell
dotnet new console -o src/OrderHub.Mcp
dotnet sln add src/OrderHub.Mcp
dotnet add src/OrderHub.Mcp package ModelContextProtocol --prerelease
dotnet add src/OrderHub.Mcp package Microsoft.Extensions.Hosting
dotnet add src/OrderHub.Mcp reference src/OrderHub.Core src/OrderHub.Infrastructure
```

### 1b. Program.cs

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderHub.Core.Interfaces;
using OrderHub.Core.Services;
using OrderHub.Infrastructure.Data;
using OrderHub.Infrastructure.Repositories;

var builder = Host.CreateApplicationBuilder(args);

// 重要:stdout 是 MCP 的協定通道,所有 log 一律走 stderr
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddDbContext<OrderHubDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")
        ?? "Server=localhost;Database=OrderHubTraining;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"));

// 與 OrderHub.Web 相同的分層接線:工具走 service / repository,不直接摸 DbContext
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<OrderHubTools>();

await builder.Build().RunAsync();
```

### 1c. 工具類別(OrderHubTools.cs)

工具類別支援建構子注入——照專案慣例注入 **service / repository**(不直接摸 DbContext),金額計算直接重用 `IOrderService` 的 `CalculateSubtotal` / `CalculateTotal`,不要在工具裡重複折扣規則:

```csharp
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
```

> 工具名稱會由 SDK 自動轉成 snake_case:`GetOrder` → `get_order`、`LowStock` → `low_stock`。

**地雷區**

- **stdout 絕對不能印東西**(`Console.WriteLine` 會打斷協定),log 一律 stderr——範本第一段就是在做這件事;煙霧測試時也要注意 **stdin 不能立刻關閉**,否則 server 會在回應送出前就關機
- Entity 直接序列化會因循環參照(Order ↔ Customer)在**執行期**炸掉——一律投影成匿名物件;這也是「編譯過 ≠ 能跑」的好教案
- 金額別自己算:折扣規則在 `OrderService` 裡,工具重複實作一份,規則改版時就會出現兩種答案

**驗證方式**:

- [ ] `dotnet build src/OrderHub.Mcp` 成功
- [ ] 一個獨立 commit(訊息說明新增了哪些工具)

---

## 練習 2 — 用 MCP Inspector 除錯

**目標**:不接 agent,先用官方檢查器手動測工具——這是 MCP 開發的標準除錯流程。

Terminal:

```powershell
npx @modelcontextprotocol/inspector dotnet run --project src/OrderHub.Mcp
```

瀏覽器會開啟 Inspector 介面:Connect → Tools → List Tools。

**驗證方式**:

- [ ] 三個工具都列得出來,且 description、參數說明如你所寫
- [ ] 手動呼叫 `LowStock`(threshold=10),回傳的商品和 `/Products` 頁面上的低庫存商品一致
- [ ] 呼叫 `GetOrder` 用一個不存在的 Id,回應是清楚的錯誤訊息而不是 exception dump

---

## 練習 3 — 註冊給 agent,做 before/after 對照

**目標**:把 server 接進你的 CLI,親眼看「有工具 vs 沒工具」的差異。

Claude Code(`training-repo/.mcp.json`,進 git 全隊共用):

```json
{
  "mcpServers": {
    "orderhub": {
      "command": "dotnet",
      "args": ["run", "--project", "src/OrderHub.Mcp"]
    }
  }
}
```

Codex(`~/.codex/config.toml`;新版 CLI 也可用 `codex mcp add` 指令管理):

```toml
[mcp_servers.orderhub]
command = "dotnet"
args = ["run", "--project", "src/OrderHub.Mcp"]
```

> `dotnet run` 首次啟動要編譯、較慢,agent 連線逾時的話先 `dotnet build` 一次,或改指向發佈後的執行檔。

**對照實驗**(重點就在這裡):

1. **關掉** MCP(Claude Code:`/mcp` 裡停用或暫時把 `.mcp.json` 改名;Codex:把 config.toml 的 `[mcp_servers.orderhub]` 區塊註解掉後重啟),問 agent:「哪些商品庫存低於 5?」——觀察它得寫程式或查 DB 繞多遠
2. **開啟** MCP,同一個問題再問一次——應該一次工具呼叫就答完
3. 把兩次的過程差異記進 PROCESS.md

**驗證方式**:

- [ ] Claude Code 輸入 `/mcp` 能看到 orderhub server 與三個工具(Codex 重啟後觀察工具可用)
- [ ] 對照實驗完成且記錄
- [ ] `.mcp.json`(或 config 片段說明)進 git,一個獨立 commit

---

## 練習 4 — 會改資料的工具:cancel_order

**目標**:前三個練習的工具全是唯讀的,agent 頂多「答錯」;這一題給它一個**會改資料庫**的工具,體會授權與人工確認從此變成設計的一部分。

狀態檢查(僅待處理/已確認可取消)與庫存回補都已經在 `OrderService.CancelOrderAsync` 裡,工具**只做轉接**,不要重複實作規則:

```csharp
[McpServerTool(Destructive = true, Idempotent = false),
 Description("取消一筆訂單(僅限待處理/已確認狀態),品項庫存會自動回補。此操作會修改資料,無法還原")]
public async Task<string> CancelOrder([Description("要取消的訂單 Id")] int id)
{
    var result = await orderService.CancelOrderAsync(id);
    return result.Success
        ? $"訂單 {id} 已取消,庫存已回補"
        : $"取消失敗:{result.ErrorMessage}";
}
```

順手做一件事:回頭把練習 1 的三個唯讀工具補上 `[McpServerTool(ReadOnly = true)]` 標註。

**地雷區**

- **標註的預設值會反咬你**:`Destructive` 預設是 `true`、`ReadOnly` 預設是 `false`——唯讀工具「懶得標」等於向 client 宣告它可能有破壞性,client 可能因此每次呼叫都跳確認
- **標註只是提示(hint),不是強制**:client 讀了標註決定要不要跳人工確認,但 server 不能假設對面一定遵守——真正的授權檢查要做在 server(或 service 層),不能外包給 client。不同 client 的行為也不同:Claude Code 會參考 annotations 決定確認時機,Codex 的 MCP 工具呼叫則主要由 `approval_policy` 管
- **錯誤訊息是 agent 的 UX**:「狀態為 Shipped 的訂單不可取消」讓 agent 能向使用者解釋並停手;stack trace 只會讓它瞎猜重試

**驗證方式**:

- [ ] MCP Inspector 中 `cancel_order` 的 annotations 如你所標(destructiveHint 等),三個唯讀工具則顯示 read-only
- [ ] 對 agent 說「幫我取消訂單 X」:觀察**權限確認提示**——你按允許之前,資料不會被動到
- [ ] 取消一筆待處理訂單成功,回 `/Products` 頁面確認庫存有回補(就是活動 1 客訴 3 修好的行為)
- [ ] 對同一筆訂單再取消一次、或挑一筆已出貨訂單取消:得到清楚的拒絕訊息而非 exception dump
- [ ] 獨立 commit;PROCESS.md 記錄

---

## 練習 5 — MCP 不是只有 tools:Resources 與 Prompts

**目標**:MCP 還有兩個常用原語——**Resource**(server 提供的唯讀資料,由 client 決定何時放進 context)與 **Prompt**(server 預先定義的提示範本,像 slash command 一樣取用)。各做一個,體會它們和 Tool 的分工差異。

### 5a. Resource — 把「背景知識」交給 agent

會員折扣規則是典型的 Resource 素材:它不是「查詢動作」(不用參數、不用打 DB),而是 agent 判讀金額問題時該有的**背景知識**。新增 `OrderHubResources.cs`:

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerResourceType]
public class OrderHubResources
{
    [McpServerResource(UriTemplate = "orderhub://discount-rules",
        Name = "會員折扣規則", MimeType = "text/markdown")]
    [Description("目前生效的會員折扣規則與計算方式")]
    public static string DiscountRules() => """
        # OrderHub 會員折扣規則
        - Standard:不打折
        - Silver:95 折
        - Gold:9 折
        折扣在訂單總額上折抵一次,單價快照(UnitPriceSnapshot)為下單當下原價。
        """;
}
```

### 5b. Prompt — 把「常用的一段話」做成一鍵範本

採購同事每週都要問一次的「低庫存採購建議」,做成 prompt 範本。新增 `OrderHubPrompts.cs`:

```csharp
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
```

Program.cs 註冊(接在 `WithTools` 後面):

```csharp
    .WithResources<OrderHubResources>()
    .WithPrompts<OrderHubPrompts>();
```

### 5c. 體會三者的分工

重新連上 server(Claude Code:`/mcp` 裡 reconnect;Codex:重啟 session)後:

1. **Resource**:輸入 `@` 應可看到 orderhub 的 resource,選取 `orderhub://discount-rules` 後問「Gold 會員買 1000 元商品應付多少?」——agent 不用讀程式碼就答對
2. **Prompt**:MCP prompts 會變成 slash command,輸入 `/mcp__orderhub__low_stock_report` 執行——觀察它展開成你寫的那段話,接著自動呼叫 `low_stock` 完成報告(**prompt 引導 agent 用 tool,這就是兩個原語的合體**)

> Codex CLI 目前只把 MCP 的 **tools** 接進對話——resources 沒有 `@` 選取介面、prompts 也不會變成 slash command。這兩個原語請用 MCP Inspector 驗證(練習 2 的流程); 3. 想一想並記進 PROCESS.md:折扣規則用 Resource 給,和讓 agent 自己去讀 `OrderService.cs`,差在哪?prompt 範本放在 server,和每個人自己打一段話,差在哪?(提示:團隊共用、版本控制、規則改版時要改幾個地方)

**地雷區**

- 分工別搞混:**Tool 是動作**(要查、要算、要改),**Resource 是資料**(讀了放進 context),**Prompt 是範本**(替使用者說話)。「什麼都做成 tool」是最常見的 MCP 設計臭味
- Resource 的內容和程式碼一樣會過期——折扣規則若寫死在 resource 字串裡,`OrderService` 改版時就有兩份真相(和練習 1「金額別自己算」同一堂課;想避免,resource 也可以動態組出內容)

**驗證方式**:

- [ ] MCP Inspector:Resources 分頁讀得到 `orderhub://discount-rules`;Prompts 分頁能帶 `threshold` 參數取得展開後的訊息
- [ ] Claude Code:`@` 選 resource 後問折扣問題,agent 用 resource 內容作答(Codex 用戶:Inspector 讀出 resource 內容貼進對話,問同一題)
- [ ] Claude Code:`/mcp__orderhub__low_stock_report` 一鍵產出採購建議表
- [ ] PROCESS.md 記錄 5c 第 3 點的思考;獨立 commit

---
