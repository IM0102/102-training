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
