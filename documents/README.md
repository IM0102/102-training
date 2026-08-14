# OrderHub 訂單管理系統（培訓專案）

公司內部訂單管理系統，做為初級 AI Agent 實作培訓的練習專案。

心得記錄範本在 **[PROCESS.md](PROCESS.md)**。

## 培訓系列地圖

| 活動 | 主題                                             | 指南                                          |
| ---- | ------------------------------------------------ | --------------------------------------------- |
| 1    | Agentic Coding:用 agent 讀懂專案、修 bug、加功能 | [活動 1](activities/activity-guideline.md)    |
| 2    | MCP Server:給 agent 造工具                       | [活動 2](activities/activity-2-custom-mcp.md) |
| 3    | Gemini 免費 API:把 AI 嵌進產品                   | [活動 3](activities/activity-3-gemini-api.md) |
| 4    | n8n 自動化:把人抽離流程                          | [活動 4](activities/activity-4-n8n.md)        |

## 技術棧

- .NET 8（ASP.NET Core MVC + Razor Views + Bootstrap 5，前端資源皆為本地檔案，不依賴 CDN）
- EF Core 8 + SQL Server（本機安裝，不使用 Docker）
- xUnit（測試使用 EF Core InMemory，**不需要** SQL Server）

## 環境需求

1. **.NET 8 SDK**（`dotnet --list-sdks` 應列出 8.x；9.x／10.x SDK 也可建置本專案）
2. **本機 SQL Server**（任一版本皆可：Developer / Express / LocalDB），並確認服務已啟動

## 啟動步驟

```powershell
git clone https://github.com/sox6769/traning.git
cd traning/training-repo
dotnet run --project src/OrderHub.Web
```

第一次啟動會自動：

1. 建立資料庫 `OrderHubTraining`（自動執行 EF Core migration）
2. 植入種子資料：20 位客戶、50 個商品、200 筆近 90 天的訂單（固定 random seed，每個人的資料一致）

看到 `Now listening on: http://localhost:xxxx` 後，用瀏覽器開啟該網址即可。

## 資料庫連線設定

預設連線字串（`src/OrderHub.Web/appsettings.Development.json`）：

```
Server=localhost;Database=OrderHubTraining;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True
```

依你的環境調整 `Server=`：

| 你的 SQL Server            | 連線字串寫法                                                                                                                        |
| -------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| 預設實例                   | `Server=localhost;...`（預設值，不用改）                                                                                            |
| 具名實例（如 SQL Express） | `Server=.\SQLEXPRESS;...`                                                                                                           |
| LocalDB                    | `Server=(localdb)\MSSQLLocalDB;...`                                                                                                 |
| SQL 帳號密碼登入           | `Server=localhost;Database=OrderHubTraining;User Id=sa;Password=你的密碼;TrustServerCertificate=True;MultipleActiveResultSets=True` |

## 頁面導覽

| 頁面     | 路由                         | 說明                                                           |
| -------- | ---------------------------- | -------------------------------------------------------------- |
| 訂單列表 | `GET /Orders`                | 狀態篩選 + 分頁（每頁 20 筆），點編號進明細                    |
| 訂單明細 | `GET /Orders/Details/{id}`   | 品項、單價快照、會員折扣與應付總額；待處理／已確認的訂單可取消 |
| 建立訂單 | `GET /Orders/Create`         | 選客戶（顯示會員等級）、動態增減商品明細列                     |
| 商品列表 | `GET /Products`              | 含**現有庫存**與販售狀態                                       |
| 客戶列表 | `GET /Customers`             | 每位客戶可點「查看訂單」                                       |
| 客戶訂單 | `GET /Customers/{id}/Orders` | 該客戶的全部訂單                                               |

表單動作（POST，皆含 anti-forgery token）：

| 動作       | 路由                       | 說明                                                             |
| ---------- | -------------------------- | ---------------------------------------------------------------- |
| 送出新訂單 | `POST /Orders/Create`      | 驗證失敗回表單顯示錯誤；成功導向明細頁                           |
| 取消訂單   | `POST /Orders/Cancel/{id}` | 明細頁的「取消訂單」按鈕；僅待處理／已確認可取消，狀態轉為已取消 |

會員折扣規則：Standard 不打折、Silver 95 折、Gold 9 折（在訂單總額上折抵一次）。

## 執行測試

```powershell
dotnet test
```

測試使用 EF Core InMemory，不需要 SQL Server、也不會動到你的資料庫。

## 專案結構與慣例

```
src/
├── OrderHub.Web/            # MVC：Controllers、ViewModels、Views（只做接線與顯示）
├── OrderHub.Core/           # Domain models、service 介面與商業邏輯（折扣、庫存、狀態轉移）
└── OrderHub.Infrastructure/ # EF Core DbContext、repositories、migrations、種子資料
tests/
└── OrderHub.Tests/          # xUnit（InMemory DB）
```

慣例（新增功能時請遵循）：

- Controller 保持薄，商業邏輯放 Core 的 service，透過 interface 注入
- 資料存取走 repository，不在 service / controller 直接摸 `DbContext`
- View 一律綁 ViewModel（mapping 手寫），不直接綁 domain model
- 伺服器端驗證用 DataAnnotations + ModelState，錯誤顯示在表單上
- 操作結果訊息用 `TempData["Success"] / TempData["Error"]`（`_Layout.cshtml` 有共用 alert 區塊）

## 疑難排解

**啟動時報 `A network-related or instance-specific error ...`（error 26 / 40）**
SQL Server 服務沒啟動或實例名稱不對。

- 開「服務」(services.msc) 確認 `SQL Server (MSSQLSERVER)` 或 `SQL Server (SQLEXPRESS)` 為「執行中」
- 具名實例請把連線字串改成 `Server=.\SQLEXPRESS`（見上表）

**報 `Login failed for user ...`**
Windows 驗證權限不足：用你目前的 Windows 帳號在 SSMS 能否連上該實例？不行的話請 IT 加權限，或改用 SQL 帳密登入格式。

**報 `CREATE DATABASE permission denied`**
你的帳號在該實例沒有建庫權限。請 DBA 授權，或先手動建立空的 `OrderHubTraining` 資料庫再啟動（migration 會自動建表）。

**port 被占用（`Failed to bind to address`）**
改用其他 port：`dotnet run --project src/OrderHub.Web --urls http://localhost:5299`

**想重置資料庫（回到初始種子資料）**

```powershell
dotnet ef database drop -f -p src/OrderHub.Infrastructure -s src/OrderHub.Web
dotnet run --project src/OrderHub.Web   # 會重新 migrate + seed
```

（`dotnet ef` 未安裝時：`dotnet tool install -g dotnet-ef`）

**HTTPS 憑證警告**
第一次跑 .NET 網站可執行 `dotnet dev-certs https --trust` 信任開發憑證。

---

## 讀物

- [如何減少token使用量](references/reduce-token-usage.md)
- [提示技巧最佳實踐](references/prompting-best-practice.md)
- [MCP 攻擊面與防禦:](references/mcp-security-attack-vectors.md)
