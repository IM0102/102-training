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
