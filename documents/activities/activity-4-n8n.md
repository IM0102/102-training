# 活動 4 — n8n 自動化:把人抽離流程

前三個活動的成果在這裡合體:用 n8n 搭一條「排程巡檢 → 查訂單 → AI 寫日報 → 分流處置」的自動化流程——查詢直接打活動 3 做好的 search API,AI Agent 節點掛活動 3 的 Gemini key,再透過 MCP 呼叫活動 2 的 OrderHub server 深挖明細。

Why n8n：

- 流程即文件,不用讀代碼溝通
- 連接器是現成的,不用自己接 SDK
- 每次執行都留痕、每個節點看得見輸入輸出
- 人機協作是內建節點,不是自己造輪子

## 前置作業:自架 n8n(npm 版)

```powershell
npx n8n
```

啟動後瀏覽器開 `http://localhost:5678`,首次進入會要求建立 owner 帳號(存在本機,跟 n8n cloud 無關)。

---

## 補齊 — MCP server 加開 HTTP transport(活動 2 的延伸)

**目標**:活動 2 的 server 走 stdio(agent 把它當本機子行程拉起來);但 **n8n 的 MCP 節點只支援 SSE / streamable HTTP,不支援 stdio**,所以練習 3 之前要幫同一個 server 加開 HTTP 入口。工具、Resource、Prompt **一行都不用改**——換的只是 transport,這正是 MCP 分層設計的紅利。

### a. 加套件與 framework reference

版本要跟 csproj 裡既有的 `ModelContextProtocol` 對齊(目前是 `2.0.0-preview.2`)——用 `--prerelease` 會抓最新的 preview.3,和鎖住的 preview.2 打架,restore 直接報 NU1605 降版錯誤:

```powershell
dotnet add src/OrderHub.Mcp package ModelContextProtocol.AspNetCore --version 2.0.0-preview.2
```

`src/OrderHub.Mcp/OrderHub.Mcp.csproj` 裡加一段(console 專案要借用 ASP.NET Core 才能開 HTTP 端點):

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

### b. Program.cs 改成雙 transport

帶 `--http` 參數走 HTTP,預設照舊走 stdio——`.mcp.json` 與 Codex 設定**完全不用動**:

```csharp
using Microsoft.AspNetCore.Builder;   // console 專案沒有 Web SDK 的 implicit usings,WebApplication 與 MapMcp 都靠這行
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderHub.Core.Interfaces;
using OrderHub.Core.Services;
using OrderHub.Infrastructure.Data;
using OrderHub.Infrastructure.Repositories;

if (args.Contains("--http"))
{
    // HTTP 版:給 n8n 等遠端 client 用,streamable HTTP 端點在 http://localhost:3001
    var builder = WebApplication.CreateBuilder(args);
    AddOrderHubServices(builder.Services, builder.Configuration);
    builder.Services.AddMcpServer()
        .WithHttpTransport(options => options.Stateless = true)
        .WithTools<OrderHubTools>()
        .WithResources<OrderHubResources>()
        .WithPrompts<OrderHubPrompts>();

    var app = builder.Build();
    app.MapMcp();
    app.Run("http://localhost:3001"); // 若果port已被暫用則另選port
}
else
{
    // stdio 版:活動 2 的原樣。stdout 是協定通道,log 一律走 stderr
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    AddOrderHubServices(builder.Services, builder.Configuration);
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<OrderHubTools>()
        .WithResources<OrderHubResources>()
        .WithPrompts<OrderHubPrompts>();

    await builder.Build().RunAsync();
}

static void AddOrderHubServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddDbContext<OrderHubDbContext>(options =>
        options.UseSqlServer(configuration.GetConnectionString("Default")
            ?? "Server=localhost;Database=OrderHubTraining;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"));

    // 與 OrderHub.Web 相同的分層接線:工具走 service / repository,不直接摸 DbContext
    services.AddScoped<ICustomerRepository, CustomerRepository>();
    services.AddScoped<IProductRepository, ProductRepository>();
    services.AddScoped<IOrderRepository, OrderRepository>();
    services.AddScoped<IOrderService, OrderService>();
}
```

啟動 HTTP 版:

```powershell
dotnet run --project src/OrderHub.Mcp -- --http
```

**驗證方式**:

- [ ] `npx @modelcontextprotocol/inspector`,Transport 選 **Streamable HTTP**、URL 填 `http://localhost:3001`:四個工具、resource、prompt 都列得出來
- [ ] 不帶 `--http` 照舊走 stdio:Claude Code `/mcp` 裡的 orderhub 一切正常
- [ ] 獨立 commit

---

## 練習 1 — Hello Webhook

**目標**:搞懂 n8n 的最小迴路:trigger → 節點 → 回應。

### 步驟

1. 左上 **+ → Create Workflow** 建新 workflow,點畫布中央的 **+** 加第一個節點,搜尋 **Webhook**
2. Webhook 節點參數這樣設:
   - **HTTP Method**:`POST`
   - **Path**:保留自動產生的亂碼即可(也可改成好記的,如 `hello`)
   - **Respond**:改選 **Using 'Respond to Webhook' Node** ⚠️ 這步最容易漏——預設值是 _Immediately_,那樣 n8n 只會回一句 `Workflow was started`,你在第 3 步接的 Respond 節點會被**完全忽略**
3. 在 Webhook 節點右側點 **+**,接一個 **Edit Fields (Set)** 節點:
   - Mode 用預設的 _Manual Mapping_,**Add Field** 新增一欄:Name 填 `receivedAt`,Type 選 _String_,Value 填 expression `{{ $now.toISO() }}`(點欄位右邊把 Fixed 切成 **Expression** 才能用 `{{ }}`)
   - 打開 **Include Other Input Fields**(選 _All_)——不開的話輸出只剩 `receivedAt` 一欄,你送進來的 body 會被丟掉
4. 再接一個 **Respond to Webhook** 節點,**Respond With** 選 _First Incoming Item_
5. 打開 Webhook 節點,複製 **Test URL**(注意上方有 Test URL / Production URL 兩個分頁,現在要用 **Test**)
6. 回到畫布按 **Execute Workflow**(或 Webhook 節點上的 _Listen for test event_)——按下去後 Test URL 才開始監聽,**只活 120 秒,而且收到一發請求就自動停止**
7. 趁監聽中,開另一個 PowerShell 視窗打:

```powershell
Invoke-RestMethod -Method Post -Uri "<n8n給的Test URL>" -Body '{"text":"hello"}' -ContentType "application/json"
```

應該回 `text=hello` 加上 `receivedAt` 時間戳,同時編輯器畫布上每個節點都亮綠勾,點開可看各節點的輸入輸出——這就是 Test 模式的價值:資料流看得見。

**驗證方式**:

- [ ] 回應含你送的內容 + 時間戳
- [ ] 理解 **Test URL vs Production URL** 的差別:Test 要在編輯器按 Listen 才活(120 秒、一發即停),**Activate** workflow 後才有常駐的 Production URL,執行紀錄看 **Executions** 分頁

---

## 練習 2 — 退單巡檢日報(主菜)

**目標**:一條端到端流程——排程觸發,查出近 30 天取消的訂單,AI 寫成日報;有退單就開 GitHub issue + 通知,沒退單就記錄歸檔。查詢直接用活動 3 的 `/api/orders/search`,**零新程式碼**。

先把網站跑起來:`dotnet run --project src/OrderHub.Web`(埠號 5150)。

### 流程設計

```
Schedule Trigger(每天 09:00;測試時直接按 Execute Workflow)
  → HTTP Request(POST http://localhost:5150/api/orders/search
                  body:{"text":"過去 30 天取消的訂單"})
  → AI Agent + Google Gemini Chat Model(credential 填活動 3 的 key,
     把查詢結果 JSON 寫成中文日報:筆數、總金額、值得注意的訂單)
  → IF 退單筆數 > 0
      ├─ true:  GitHub 節點(在 training repo 開 issue,標題=日報第一行,內文=完整日報)
      │         + Slack/Teams 通知(沒有工作區就用 Email 或再打一個 webhook 代替)
      └─ false: Data Table 插一列紀錄(「本日無退單」)
```

### 步驟

1. 新 workflow,加 **Schedule Trigger** 節點:
   - **Trigger Interval**:_Days_
   - **Days Between Triggers**:`1`
   - **Trigger at Hour**:_9am_
   - 排程要等 workflow **Activate** 後才會真的跑——整個開發期間都直接按 **Execute Workflow** 手動觸發即可
2. 接 **HTTP Request** 節點:
   - **Method**:`POST`
   - **URL**:`http://localhost:5150/api/orders/search`
   - **Send Body**:打開
   - **Body Content Type**:_JSON_
   - **Specify Body**:_Using JSON_,**JSON** 填 `{ "text": "過去 30 天取消的訂單" }`
   - 節點視窗上方切到 **Settings** 分頁,打開 **Always Output Data** ⚠️ 不開的話:API 查無資料時回空陣列 `[]`,這個節點會輸出**零個 item**,下游節點(含 IF)根本不會執行,「歸檔分支」永遠走不到
   - 按 **Execute step** 試打一發:API 回 JSON 陣列,n8n 會把陣列拆成一列一個 item,每個 item 有 `id`、`customerName`、`tier`、`status`、`total`、`createdAt` 六欄
3. 接 **Code** 節點(JavaScript):
   - 節點改名為 `整理筆數`(點開節點、點標題即可改)——第 5 步的 expression 會引用這個名字
   - **Mode**:預設的 _Run Once for All Items_
   - 程式碼:

   ```js
   // Always Output Data 在空結果時會給一個空 item,先濾掉再算筆數
   const orders = $input
     .all()
     .map((i) => i.json)
     .filter((o) => o.id !== undefined);
   return [{ json: { count: orders.length, orders } }];
   ```

   輸出從「N 個訂單 item」變成**單一 item** `{count, orders}`,後面的 AI 與 IF 都吃這個

4. 接 **AI Agent** 節點:
   - **Source for Prompt (User Message)**:_Define below_
   - **Prompt**:切成 Expression,填 `以下是近 30 天取消訂單的查詢結果 JSON(count 為筆數):{{ JSON.stringify($json) }}`
   - **Options → Add Option → System Message**:「你是 OrderHub 的退單巡檢助理。只根據提供的資料寫中文日報,不要編造數字。第一行用一句話摘要(會當 issue 標題),其後列出:總筆數、總金額、值得注意的訂單(高金額或高等級會員)。count 為 0 時只回『本日無退單』。」
   - 點節點下方 **Chat Model** 的 **+**,選 **Google Gemini Chat Model**:
     - **Credential**:_Create new credential_,API Key 填活動 3 的 key 存檔
     - **Model**:`gemini-3.5-flash`(跟活動 3 同一顆,免費層)
   - AI Agent 的輸出只有一個 `output` 欄位(日報全文),後面節點都用 `$json.output` 拿
5. 接 **IF** 節點,**Add Condition**:
   - 左值:切成 Expression,填 `{{ $('整理筆數').first().json.count }}`——AI Agent 的輸出裡已經沒有 `count`,所以用 `$('節點名')` 跨節點回頭拿第 3 步的結果
   - 條件型別:_Number_,operator 選 _is greater than_
   - 右值:`0`
6. **true 分支**接 **GitHub** 節點:
   - **Credential**:_Create new_,填 GitHub 帳號 + token
     - token 這樣拿:GitHub 網站 → Settings → Developer settings → Personal access tokens → **Generate new token (classic)**,勾 **repo** scope
   - **Resource**:_Issue_
   - **Operation**:_Create_
   - **Repository Owner / Name**:選你的 training repo(清單裡沒有就改選 _By URL_,貼 repo 的 GitHub 網址)
   - **Title**:切 Expression,填 `{{ $json.output.split('\n')[0] }}`(日報第一行)
   - **Body**:填 `{{ $json.output }}`
7. GitHub 節點後面再接一個 **HTTP Request** 節點(當通知用):
   - 先把練習 1 那條 workflow **Activate** 起來——正好體會 Production URL 的用途
   - **Method**:`POST`
   - **URL**:練習 1 的 **Production URL**
   - **Send Body**:打開,**Body Content Type** 選 _JSON_,**Specify Body** 用預設的 _Using Fields Below_,加兩欄:
     - Name 填 `report`,Value 切 Expression 填 `{{ $('AI Agent').first().json.output }}`
     - Name 填 `issueUrl`,Value 切 Expression 填 `{{ $json.html_url }}`——GitHub 的輸出在這裡反而好用,通知直接帶上剛開的 issue 連結
8. **false 分支**負責留痕:「本日無退單」也要留下紀錄,用 n8n 內建的 **Data Table**(資料存在 n8n 自己的資料庫,零 credential、零環境變數):
   - 先建表:左側 **Overview → Data tables** 分頁 → **Create Data table**,表名 `巡檢紀錄`,加兩個欄位:`date`(String)、`note`(String)
   - 回到 workflow,false 分支接 **Data Table** 節點:
     - **Operation**:_Insert_
     - **Data Table**:_From list_ 選 `巡檢紀錄`
     - 欄位 **date**:切 Expression,填 `{{ $now.toFormat('yyyy-MM-dd') }}`
     - 欄位 **note**:填 `本日無退單`
   - 跑完到 **Data tables** 分頁點開表,應該多一列今天的紀錄——這就是歸檔證據

流程圖：
![workflow](../references/n8n-flow.png)

### 分工是刻意的

- **查詢在產品程式碼裡**:HTTP Request 打的是活動 3 的 API——白名單參數、防注入、上限保險都在那裡做完了,n8n 只做編排。這是「業務邏輯放哪」的正解示範
- **AI 只做摘要**:Gemini 拿到的是查詢結果 JSON,不碰資料庫、不決定查什麼。System Message 記得要求「只根據提供的資料寫日報,不要編造數字」

**驗證方式**:

- [ ] 先準備素材:在網站取消一筆待處理訂單(或用活動 2 的 `cancel_order`——對 agent 說「幫我取消訂單 X」,順便複習權限確認)
- [ ] Execute workflow:開出 GitHub issue、收到通知;日報數字和 `/Orders` 頁面篩「已取消」肉眼比對一致
- [ ] 把查詢文字改成查不到東西的條件(例如「昨天取消的訂單」):存進n8n Datatable,不開 issue
- [ ] 在 PROCESS.md 回答:如果「查什麼、怎麼查」也交給 AI Agent 自由發揮,會失去什麼?(提示:活動 3 的白名單防線、可測試性、日報數字的可信度)

---

## 練習 3 — MCP 合體:讓流程裡的 AI 會用你的工具

**目標**:日報不只列清單,還要「深挖」——AI Agent 用活動 2 的 MCP 工具查每筆退單的明細,日報引用真實品項與金額。

### 步驟

1. 啟動補課的 HTTP 版 MCP server:`dotnet run --project src/OrderHub.Mcp -- --http`。
2. 打開練習 2 的 AI Agent 節點,點下方 **Tool** 的 **+**,選 **MCP Client Tool**:
   - **Endpoint**:`http://localhost:3001`(若你在 `MapMcp("/mcp")` 自訂過路由,URL 跟著改)
   - **Server Transport**:選 **HTTP Streamable**
   - **Authentication**:_None_(補課的 server 沒設驗證)
   - **Tools to Include**:選 _Selected_,只勾 `get_order`。巡檢流程只需要「讀」;`cancel_order` 這種寫入工具**絕不掛進無人流程**——活動 1 的 approval 哲學,在這裡的形狀就是「根本不給工具」
3. **AI Agent node**: System Message 加一句「對每筆取消的訂單,先用工具查出品項明細與會員等級,日報中引用查到的實際數字」
4. Execute Workflow 後,到 **Executions** 分頁點開這次執行,再點 AI Agent 節點:右側 log 會列出 agent 每一次工具呼叫的輸入與輸出,這是驗證「有沒有真的深挖」的直接證據

**驗證方式**:

- [ ] 執行紀錄裡看得到 agent 對退單呼叫了 `get_order`,日報引用了真實品項與金額
- [ ] 對照練習 2:同一批退單,有深挖 vs 沒深挖的日報差異,記進 PROCESS.md

---
