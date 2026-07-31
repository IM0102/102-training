# 練習 3 — MCP Server 註冊與 Before/After 對照

## 設定

`.mcp.json`(repo 根目錄,進 git 全隊共用):

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

驗證:`dotnet build src/OrderHub.Mcp` 先跑過一次(避免 agent 連線逾時),`tools/list` 可看到三個工具:`get_order`、`low_stock`、`customer_orders`(注意:C# 方法名是 PascalCase,MCP SDK 會自動轉成 snake_case 對外曝露)。

## 對照實驗:「哪些商品庫存低於 5?」

### Before(沒有 MCP 工具)

沒有工具時,要回答這個問題得自己繞一圈基礎設施:

1. 先找 DB 連線字串 —— 翻 `src/OrderHub.Web/appsettings.json` 才知道 DB 叫 `OrderHubTraining`、用 Windows 驗證登入 `localhost`。
2. 不知道表結構,得先查 `INFORMATION_SCHEMA.TABLES` 確認有 `Products` 表。
3. 再查 `INFORMATION_SCHEMA.COLUMNS` 確認欄位名稱(`Sku`、`Name`、`StockQuantity`、`IsActive`)。
4. 自己手刻 SQL,還要記得複製業務規則(`IsActive = 1` 且仍在販售、依庫存量升冪排序 —— 這條規則寫在 `OrderHubTools.LowStock` 裡,沒工具的話等於要重新讀 code 才知道):
   ```sql
   SELECT Sku, Name, StockQuantity FROM Products
   WHERE IsActive = 1 AND StockQuantity < 5
   ORDER BY StockQuantity ASC
   ```
5. 用 `sqlcmd` 執行,還遇到主控台編碼問題(中文商品名變亂碼,需要額外處理輸出編碼)。

共 5 個步驟、3 次探索性查詢 + 1 次正式查詢,而且業務規則(哪些狀態算「仍在販售」)是用猜的/翻 code 才確定,容易漏掉或猜錯。

### After(開啟 MCP)

一次工具呼叫直接拿到答案:

```
low_stock(threshold=5)
```

```json
[
  { "Sku": "SKU-1048", "Name": "晨光 行動電源", "StockQuantity": 2 },
  { "Sku": "SKU-1005", "Name": "極光 筆電支架", "StockQuantity": 3 },
  { "Sku": "SKU-1023", "Name": "雲峰 27吋螢幕", "StockQuantity": 3 },
  { "Sku": "SKU-1014", "Name": "星河 USB-C 集線器", "StockQuantity": 4 },
  { "Sku": "SKU-1032", "Name": "曜石 機械鍵盤", "StockQuantity": 4 }
]
```

不需要知道連線字串、表名、欄位名,`IsActive` 篩選與排序邏輯也已經封裝在工具裡,結果與 Before 手刻 SQL 完全一致,但只花 1 次呼叫、UTF-8 輸出無亂碼問題。

### 差異總結

| | Before(無 MCP) | After(有 MCP) |
|---|---|---|
| 需要的背景知識 | DB 連線字串、表名、欄位名、業務規則(IsActive 過濾) | 無(工具描述即文件) |
| 步驟數 | 5(2 次 schema 探索 + 1 次查連線設定 + 1 次正式查詢 + 1 次編碼排錯) | 1(直接呼叫工具) |
| 業務規則正確性 | 需自行翻 `OrderHubTools.cs` 原始碼確認,容易漏掉或版本落後 | 由工具保證,規則變更時工具行為自動跟著更新 |
| 輸出格式 | sqlcmd 主控台輸出,中文亂碼需額外處理 | 結構化 JSON,UTF-8 正確顯示 |

# 練習 4 — 會改資料的工具:CancelOrder 與授權/人工確認

前三個練習的工具都是唯讀的,agent 頂多「答錯」。這一題的 `cancel_order` 會真的修改資料庫(取消訂單、回補庫存),重點從「答得對不對」變成「要不要讓它做、做壞了怎麼辦」。

## 實作

`CancelOrder` 是薄轉接層,狀態檢查與庫存回補規則都留在 `OrderService.CancelOrderAsync`,工具本身不重複實作:

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

順手把練習 1 的三個唯讀工具補上 `[McpServerTool(ReadOnly = true)]`——標註預設值是 `Destructive = true`、`ReadOnly = false`,懶得標等於向 client 宣告「這個工具可能有破壞性」,client 可能因此每次呼叫都跳確認。

## 驗證方式

- **Annotations 正確性**:用 `npx @modelcontextprotocol/inspector --cli --config .mcp.json --server orderhub --method tools/list --format json` 直接列出四個工具的 annotations,確認 `get_order`/`low_stock`/`customer_orders` 都是 `"readOnlyHint": true`,`cancel_order` 是 `"destructiveHint": true, "idempotentHint": false`。annotations 只是給 client 的提示(hint),server 不能假設對面一定遵守——真正的授權判斷(能不能取消、狀態允不允許)還是留在 `OrderService.CancelOrderAsync` 裡做,不外包給 client。

- **人工確認流程**:對 agent 說「幫我取消訂單 204」,agent 先呼叫 `get_order` 查出訂單狀態(Pending)、客戶、品項、金額,列出來讓人確認後才呼叫 `cancel_order`——這一步不是工具本身強制的,是 agent 這一層自己加的確認動作,對照的是 client 端(Claude Code / Codex)看到 `Destructive` 標註後跳出的授權提示,兩者是不同層的保護,缺一不可。

- **成功案例(狀態轉移 + 庫存回補)**:訂單 204(Pending,SKU-1001 × 2)呼叫 `cancel_order` 後回傳「訂單 204 已取消,庫存已回補」;再查一次 `get_order` 確認 `Status` 變成 `Cancelled`。庫存回補這條規則本身有自動測試覆蓋(`tests/OrderHub.Tests/OrderServiceCancelTests.cs` 的 `CancelOrder_ActiveOrder_RestoresProductStock`,驗證取消後庫存精確回到取消前的數字),工具只是轉接,不需要重複驗證數字,但可以在 `/Products` 頁面或 `SELECT StockQuantity FROM Products WHERE Sku='SKU-1001'` 肉眼再確認一次。

- **拒絕案例(錯誤訊息而非 exception dump)**:對同一筆已取消的訂單 204 再呼叫一次 `cancel_order`,回傳的是清楚的業務訊息:
  ```
  取消失敗:狀態為 Cancelled 的訂單不可取消
  ```
  沒有動到任何資料,也沒有 stack trace——agent 可以直接把這句話說給使用者聽,而不是瞎猜要不要重試。`Idempotent = false` 標註在這裡也得到驗證:同一個輸入(訂單 204)第二次呼叫並不會得到跟第一次一樣的「成功」結果。

# 練習 5 — Resource 與 Prompt:Tool 以外的兩個原語

新增 `OrderHubResources.cs`(`orderhub://discount-rules`,`text/markdown`)與 `OrderHubPrompts.cs`(`low_stock_report`),`Program.cs` 接上 `.WithResources<OrderHubResources>()`、`.WithPrompts<OrderHubPrompts>()`。

## 驗證(MCP Inspector,practice 2 的 CLI 流程)

- `resources/list` 讀得到 `orderhub://discount-rules`(name「會員折扣規則」、`text/markdown`);`resources/read` 拿到完整折扣規則內容。
- `prompts/list` 讀得到 `low_stock_report`,帶一個非必填參數 `threshold`;`prompts/get --prompt-args "threshold=5"` 展開後,`{threshold}` 正確代入成 5:「請用 low_stock 工具(threshold=5)查出低庫存商品...」。

## 三者分工的思考

**折扣規則用 Resource 給,和讓 agent 自己去讀 `OrderService.cs`,差在哪?**

讓 agent 自己讀 code 也答得出來,但每次都要花 token 讀整個 `OrderService.cs`、還得自己從 `GetDiscountRate` 的 switch 語法推回「Gold 是 9 折」這種人話,推論路徑長、容易斷章取義(例如漏看折扣只在總額套用一次、單價快照不受影響這件事)。Resource 是把這段推論預先做好、寫成人看得懂的敘述,agent 直接讀敘述就能用,不用逆向工程程式碼。但這個好處是有代價的:**`OrderHubResources.cs` 裡的折數是寫死的字串,和 `OrderService.GetDiscountRate` 的實際數字是兩份真相**——這正是 CLAUDE.md 強調「金額別自己算,一律走 `CalculateTotal`/`GetDiscountRate`」同一堂課的翻版,只是這次換成 Resource 层也會過期。目前手動確認過兩邊數字一致(Standard 0%、Silver 5%、Gold 10%),但沒有任何機制保證未來改折扣時兩邊會一起改,只在程式碼加了一行提醒註解。更保險的做法是讓 `DiscountRules()` 動態組字串(例如遍歷 `CustomerTier` 列舉、呼叫 `GetDiscountRate` 產生數字,只把說明文字寫死),但這次先照練習給的靜態版本做,把這個取捨記下來。

**prompt 範本放在 server,和每個人自己打一段話,差在哪?**

自己打的話,每個人問法不同、有人會漏掉「查完 low_stock 後還要看近期訂單狀況」這個步驟,有人問完只拿到一堆原始 JSON、沒有整理成採購建議表。範本放 server 端,等於把「怎麼問」這件事也一起版本控制:全隊用同一個 `/mcp__orderhub__low_stock_report`,改進問法(例如日後想多加「同時列出上次補貨日期」)只需要改 `OrderHubPrompts.cs` 一個地方,不用去說服每個人重新打一次咒語,也不會出現「同樣的問題、五種問法、五種品質不一的答案」。這和 Tool 把業務邏輯集中在 `OrderService` 是同一個道理:重複的知識(不管是計算規則還是問法)只放一個地方,改一次全隊生效。

**分工總結**:Tool 是動作(low_stock 查、cancel_order 改),Resource 是背景知識(折扣規則,讀了放進 context),Prompt 是把「常問的一句話」模板化、變成一鍵指令。三者都是為了同一件事:不要讓每個使用者自己去重新發明「該怎麼問、該用什麼規則判斷」。
