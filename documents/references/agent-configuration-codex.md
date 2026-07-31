# 進階指南 — 把 Codex CLI「調教」成你的專案隊友

> 這份指南教你**怎麼設定 agent 的環境**——
> 用設定檔讓它自動遵守專案慣例、擋掉危險操作、把重複的流程變成一鍵指令。
> 內容與 [agent-configuration.md](agent-configuration.md)（Claude Code 版）一一對應，用哪個工具就看哪份。
> 每一節都有可以直接複製到本專案的範例，建議邊讀邊做。

**你會建立的檔案總覽**

| 檔案                             | 用途                                                    | 要不要進 git          |
| -------------------------------- | ------------------------------------------------------- | --------------------- |
| `AGENTS.md`                      | 專案記憶：agent 每次啟動自動讀的專案說明與慣例          | ✅ 進 git（全隊共用） |
| `.codex/config.toml`             | 專案層設定：hooks 等（approval / sandbox 在專案層無效） | ✅ 進 git             |
| `.codex/rules/*.rules`           | 指令規則：哪些指令直接放行、詢問、直接擋掉              | ✅ 進 git             |
| `.codex/agents/*.toml`           | Subagents：專職的子代理（如 code reviewer）             | ✅ 進 git             |
| `.agents/skills/<名稱>/SKILL.md` | Skills：把流程做成可重複觸發的指令                      | ✅ 進 git             |
| `~/.codex/config.toml`           | 個人全域設定：approval / sandbox 等安全設定放這裡       | ❌ 在家目錄，不進 git |

**與 Claude Code 的對照**（兩邊都玩過的人看這張表就懂）

| 概念           | Claude Code                               | Codex CLI                                                                                 |
| -------------- | ----------------------------------------- | ----------------------------------------------------------------------------------------- |
| 專案記憶       | `CLAUDE.md`                               | `AGENTS.md`                                                                               |
| 權限規則       | `.claude/settings.json` 的 allow/ask/deny | `approval_policy` + `sandbox_mode` + `.codex/rules/`（execpolicy）                        |
| Hooks          | settings.json 的 `hooks`                  | `.codex/config.toml` 或 `.codex/hooks.json` 的 `hooks`                                    |
| Subagents      | `.claude/agents/*.md`（Markdown）         | `.codex/agents/*.toml`（TOML）                                                            |
| 斜線指令／流程 | `.claude/skills/*/SKILL.md`               | `.agents/skills/*/SKILL.md`（舊的 `~/.codex/prompts/` custom prompts 官方文件已不再收錄） |

> **注意**：專案層的 `.codex/` 設定要在你**信任（trust）這個專案**之後才會載入——第一次在 repo 裡啟動 `codex` 時會問你是否信任此資料夾。

---

## 1. `AGENTS.md` — 讓 agent 不用每次重新認識專案

Codex 的專案記憶檔叫 **`AGENTS.md`**（跨工具的開放格式，Codex 原生支援）。放在**專案根目錄**（和 `.sln`、`.csproj` 同層），agent 每個 session 開始時自動載入。把它想成「**AI 隊友的 onboarding 文件**」：一份會進版控、全隊共用、隨程式碼一起演進的常駐指示。把「每次都要重講一遍」的東西寫進來，agent 就不必每次重新摸索專案。

### 先用 `/init` 產一份草稿

```
codex       # 啟動（首次會要求登入，並詢問是否信任此專案）
/init       # 在對話框輸入，agent 會掃描專案自動產生 AGENTS.md
```

⚠️ `/init` 產出的是**起點不是終點**——它只是把它掃到的東西整理成文，你要手動精修：補上它猜不到的慣例、刪掉冗詞。

### AGENTS.md 該寫什麼（六個區塊）

不用面面俱到，但這六塊涵蓋最常「重講一遍」的內容：

1. **專案簡介**（2–3 句）：這是什麼系統、給誰用、大概規模——讓 agent 不會用錯複雜度（小內部系統不需要它套微服務那套）
2. **技術棧與關鍵版本**：框架、資料庫、測試框架的**確切版本**——版本會直接改變寫法（例如 .NET 8 和 .NET 10 的 API 不一樣）
3. **分層與慣例**：命名、資料夾結構、每層職責、驗證與金額處理方式
4. **常用指令**：build / test / run 怎麼下
5. **重要 / 危險檔案**：碰到要特別小心的檔（migration、設定檔）——避免它「清理 API 路由」時順手改壞你的 webhook
6. **不要做的事（Don'ts）**：明確的護欄，例如「不要未經同意就裝新套件」

`training-repo/AGENTS.md` 的範例：

```markdown
# OrderHub — 專案記憶

## 專案簡介

公司內部訂單管理系統：業務可建立/查詢訂單、管理商品與客戶。
內部使用、單一 SQL Server 資料庫，不需要考慮多租戶或高併發架構。

## 技術棧

- .NET 8 / ASP.NET Core MVC（Razor Views）
- EF Core 8 + SQL Server
- 測試：xUnit

## 分層與慣例

- 三層：`OrderHub.Web`（Controller/View/ViewModel）→ `OrderHub.Core`
  （Domain/Services/Interfaces）→ `OrderHub.Infrastructure`（Repositories/Migrations）
- Controller 保持薄，只轉接 service 結果；商業邏輯一律放 Core 的 service
- 只有 repository 碰 `DbContext`；Controller / Service 不可直接用 EF Core
- Service 回傳 `ServiceResult<T>`，用它表達預期內的失敗，不要丟例外
- View 綁 ViewModel，不要把 domain model 直接丟給 View
- 使用者輸入用 DataAnnotations + ModelState 驗證；輸入錯誤絕不能變成 500
- 金額一律用 `decimal`；折扣集中在 `OrderService.CalculateTotal`，不要在別處重算
- 參考檔：Controller 照 `ProductsController.cs`、Service 照 `ProductService.cs` 的寫法

## 常用指令

- `dotnet build`：建置
- `dotnet test`：跑全部測試
- `dotnet run --project src/OrderHub.Web`：啟動網站（http://localhost:5150）

## 重要 / 危險檔案

- `src/OrderHub.Infrastructure/Migrations/**`：EF migration 是歷史紀錄，不要手改
- `src/OrderHub.Web/appsettings.json`：連線字串等設定，改動前先問

## 子代理與測試

- 使用者要求執行測試時，必須委派給自訂 agent test_runner
- test_runner 回報測試全部通過時，主代理必須原樣轉交結果，不可補充、改寫或附加其他摘要

## 不要做的事

- 不要未經同意就加新的 NuGet 套件
- 不要在 Controller / Service 直接使用 DbContext
- 不要為了「順手」重構與當前任務無關的程式碼
- 不要讀取或寫入任何機密檔（\*.pfx、appsettings.Production.json、user-secrets）
```

### 保持精簡（很重要）

AGENTS.md 每個 session 都會被讀進 context、佔 token，**寫越長、每次對話越貴，重點也越容易被稀釋**。

- **長度**：多數專案 50–100 行剛好，上限抓 200–400 行；再長就該拆檔（見下）
- **不要寫**：通用的程式建議（「要寫乾淨的程式」）、專案沿革、每週都在變的細節
- **具體 > 抽象**：與其「遵循乾淨架構」，不如寫「商業邏輯放 Core service、Controller 只轉接」並指一個範例檔——agent 照著抄比照著猜準得多

### 讓它持續進化

- **從失敗回補**：每次 agent 做錯或跑偏，問自己「**哪一句 context 能避免這次**」，把那句補進 AGENTS.md——這是它變強最快的方式
- **直接編輯就好**：Codex 沒有行內的「一鍵記憶」捷徑，AGENTS.md 就是一份純文字檔，養成隨手補一行的習慣即可；重跑 `/init` 會整份重掃、蓋掉你的手改，慎用
- **用巢狀 AGENTS.md 拆檔**：內容太長時，把局部慣例移到子目錄自己的 `AGENTS.md`，根目錄那份只留全域慣例——Codex 會依「專案根 → 目前工作目錄」沿路把每一層的 AGENTS.md 串接載入

### 檔案階層（誰蓋過誰）

Codex 載入順序是**先全域、後專案，越深越優先**：

- **全域個人偏好**放 `~/.codex/AGENTS.md`，對你所有專案生效（例如「我的 SQL Server 是具名實例 .\SQLEXPRESS」這種個人環境差異）；它會排在專案檔之前載入
- **專案根目錄**一份（以 `.git` 為界）；Codex 從專案根往下、把沿路每一層的 AGENTS.md 串接進來，**不會越過專案根往上找**
- **子目錄**可各放一份寫該模組的局部慣例；和上層衝突時**越深的越優先**
- ⚠️ 你在 prompt 裡直接下的指示，永遠**蓋過** AGENTS.md 的內容

**驗證方式**：

- [ ] 建好後開新 session，問 agent「這個專案的分層慣例是什麼？」——它應該不用讀任何檔案就答得出來
- [ ] 問「金額要用什麼型別？折扣在哪裡算？」——答案應該和 AGENTS.md 一致
- [ ] 故意請它「裝一個新 NuGet 套件」——它應該先問你，而不是逕自安裝

---

## 2. Approval + Sandbox + Rules — 先劃紅線，再開綠燈

Claude Code 用一份 allow/ask/deny 清單管權限；Codex 拆成**三層**，各管一件事：

1. **`sandbox_mode`**：OS 層沙盒，管 agent「技術上做得到什麼」（能寫哪裡、能不能連網）
2. **`approval_policy`**：管「什麼時候要問你」
3. **execpolicy rules**：對**個別指令**做細部規則——危險的直接擋掉（forbidden）、日常的直接放行（allow）、重大的強制詢問（prompt）

### 2a. 基本設定（`~/.codex/config.toml`）

在**使用者層** `~/.codex/config.toml` 設定（就是家目錄那份，不是專案層）：

```toml
# 沙盒：只能寫 workspace 內的檔案，預設不能連網
sandbox_mode = "workspace-write"

# 要跳出沙盒（連網、寫 workspace 外）或碰到 rules 標記的指令時詢問
approval_policy = "on-request"
```

> **為什麼不是放專案層？** `sandbox_mode` 和 `approval_policy` 屬於安全設定，**寫在專案層 `training-repo/.codex/config.toml` 會被 Codex 忽略**（官方 config reference 明列專案層不允許覆寫這些 key）。專案層 config 適合放 hooks 這類可以進 git 共用的設定。

- `sandbox_mode` 可選 `read-only`（只能讀）、`workspace-write`（預設，可寫工作區）、`danger-full-access`（拆掉沙盒，**不要用**）
- `approval_policy` 可選 `untrusted`（除了安全的讀取操作外都問）、`on-request`（要升權時問）、`never`（都不問，**練習中不要用**）
- 沙盒內預設**擋網路**；真的需要時才在 `[sandbox_workspace_write]` 加 `network_access = true`

> **Windows 注意**：Codex 的沙盒在 macOS 是 Seatbelt、Linux 是 `bwrap` + `seccomp`（0.115 起，WSL1 已不支援），原生 Windows 的沙盒支援較新、模式不同（unelevated / elevated），官方另推薦 WSL2。本練習在 Windows 上請以 `approval_policy` + rules 為主要防線，並實際驗證沙盒行為。

### 2b. 指令規則（`.codex/rules/orderhub.rules`）

Rules 用 Starlark 語法（長得像 Python）寫**前綴比對**規則。在 `training-repo/.codex/rules/orderhub.rules` 建立：

```python
# ---- 危險操作：直接擋掉 ----
prefix_rule(
    pattern = ["rm", "-rf"],
    decision = "forbidden",
    justification = "禁止遞迴強制刪除",
)
prefix_rule(
    pattern = ["git", "push", "--force"],
    decision = "forbidden",
    justification = "禁止強推，需要時請人工操作",
)
prefix_rule(
    pattern = ["git", "reset", "--hard"],
    decision = "forbidden",
    justification = "會丟失未 commit 的變更",
)

# ---- 重大操作：強制詢問 ----
prefix_rule(
    pattern = ["dotnet", "ef", "database", "drop"],
    decision = "prompt",
    justification = "重置資料庫必須由人確認",
)
prefix_rule(
    pattern = ["git", "push"],
    decision = "prompt",
    justification = "推上遠端前先確認",
)

# ---- 日常操作：直接放行 ----
prefix_rule(pattern = ["dotnet", "build"], decision = "allow", justification = "日常建置")
prefix_rule(pattern = ["dotnet", "test"],  decision = "allow", justification = "日常測試")
prefix_rule(pattern = ["dotnet", "run"],   decision = "allow", justification = "啟動網站")
prefix_rule(pattern = ["git", "status"],   decision = "allow", justification = "唯讀")
prefix_rule(pattern = ["git", "diff"],     decision = "allow", justification = "唯讀")
prefix_rule(pattern = ["git", "log"],      decision = "allow", justification = "唯讀")
prefix_rule(pattern = ["git", "add"],      decision = "allow", justification = "日常提交流程")
prefix_rule(pattern = ["git", "commit"],   decision = "allow", justification = "日常提交流程")
```

**規則語法重點**

- `pattern` 是**前綴比對**：`["dotnet", "build"]` 匹配所有以 `dotnet build` 開頭的指令
- 多條規則同時命中時，取**最嚴格**的決定：`forbidden` > `prompt` > `allow`——所以 `git push --force` 被 forbidden 擋下，一般 `git push` 走 prompt
- 注意：前綴比對不是完整沙盒，串接指令（如 `;`、`&&`）可能繞過，不應視為絕對安全防線——這也是為什麼還需要 sandbox 和 hooks
- Rules 只管**指令**；Claude Code 版裡的檔案層保護（禁止讀 `appsettings.Production.json`、`*.pfx`，禁止改 Migrations）在 Codex 要靠 sandbox（限制可寫範圍）與 hooks（見下一節）處理
- 寫完可以**離線測試**規則檔（不用真的啟動 agent）：

```powershell
codex execpolicy check --pretty --rules .codex/rules/orderhub.rules -- git push --force
```

**為什麼這樣設計**

- `forbidden` git reset --hard：「順手清理一下」會毀掉你還沒 commit 的練習成果
- `prompt` database drop：練習中真的需要重置資料庫，但必須是**人**按下確認
- `allow` build/test：這些指令 agent 會反覆執行，每次都問只會讓你麻木地按 yes——**權限疲勞正是事故的來源**

**驗證方式**：

- [ ] 用 `codex execpolicy check ... -- git push --force` 確認結果是 `forbidden`
- [ ] 請 agent 執行 `dotnet test`，應直接執行不詢問（allow）
- [ ] 請 agent 重置資料庫（`dotnet ef database drop`），應先跳出確認才執行（prompt）

---

## 3. Hooks — 用程式強制執行，不靠 agent 自覺

AGENTS.md 裡的規則 agent「通常」會遵守；hooks 則是**由 Codex 本身強制執行**的檢查點，agent 想繞也繞不過。Codex 的 hooks 可以寫在 `.codex/config.toml` 的 `[hooks]` 區塊，或獨立的 `.codex/hooks.json`（JSON 格式與 Claude Code 幾乎相同）。

**常用事件**：`PreToolUse`（工具執行前，可攔截）、`PostToolUse`（工具執行後）、`UserPromptSubmit`（你送出訊息時）、`SessionStart`、`Stop`（agent 結束回合時）。

rules 擋的是「指令長相」，hook 可以檢查**內容**。

### PreToolUse & PostToolUse 範例

在 `training-repo/.codex/hooks.json` 建立：

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "powershell -NoProfile -File .codex/hooks/block-destructive-sql.ps1"
          }
        ]
      }
    ],
    "PostToolUse": [
      {
        "matcher": "apply_patch",
        "hooks": [
          {
            "type": "command",
            "command": "powershell -NoProfile -File .codex/hooks/log-edits-codex.ps1",
            "statusMessage": "Logging file edit..."
          }
        ]
      }
    ]
  }
}
```

- `PreToolUse` 攔截任何含 `DROP TABLE` / `TRUNCATE` 的指令（**exit code 2 = 擋下這次工具呼叫**，stderr 訊息會回饋給 agent；其他非 0、非 2 的 exit code 視為 hook 本身故障，Codex 會照常繼續）
- `PostToolUse` 每次 agent 用 `apply_patch` 改完檔案，就把「時間、工具、檔案路徑」記錄到 `.codex/hooks/edit-log.txt`，並用 stdout JSON 的 `systemMessage` 在 UI 顯示一行提示——留下 agent 動過哪些檔案的稽核軌跡。注意 Codex 的檔案編輯工具叫 `apply_patch`（不是 Claude 的 Edit/Write）

> 兩個範例可以合併在同一個 hooks.json 裡（`PreToolUse` 和 `PostToolUse` 並列）。
> 複雜邏輯建議寫成獨立腳本放 `.codex/hooks/`，hook 的 `command` 再去呼叫它。

把以下powershell 文件拷貝到 `training-repo/.codex/hooks/`

- [block-destructive-sql.ps1](../activities/scripts/block-destructive-sql.ps1)
- [log-edits.ps1](../activities/scripts/log-edits.ps1)

**驗證方式**：
打開新session

- [ ] 設定好後，故意請 agent「執行 sqlcmd 把 OrderItems 資料表 TRUNCATE 掉」——它應該被 hook 擋下，並回報被擋的原因。
- [ ] 請agent 創建一份 sample.txt, 查看 `.codex/hooks/` 資料夾裡面出現一個 `edit-log.txt` 文件

---

## 4. Subagents — 建立專職的子代理

Subagent 是有**獨立 context、獨立沙盒權限**的子代理。兩個經典用途：

1. **唯讀 reviewer**：`sandbox_mode = "read-only"`，物理上不可能改壞你的程式碼
2. **隔離大量輸出**：跑測試、查大量檔案的雜訊留在子代理，不塞爆主對話

Codex 的 subagent 是**一個 agent 一個 TOML 檔**。在 `training-repo/.codex/agents/code-reviewer.toml` 建立：

```toml
name = "code_reviewer"
description = "審查程式碼變更是否符合 OrderHub 分層慣例。完成 bug 修復或新功能後主動使用。"
sandbox_mode = "read-only"
developer_instructions = """
你是 OrderHub 專案的資深 reviewer。審查目前的變更（git diff），依序檢查：

1. 分層：商業邏輯是否在 Core 的 service？Controller 是否保持薄？
   有沒有在 service/controller 直接使用 DbContext？
2. View 是否綁 ViewModel 而非 domain model？
3. 驗證是否用 DataAnnotations + ModelState（使用者輸入不可造成 500）？
4. 金額是否使用 decimal？
5. 有沒有對應的測試？測試是否真的驗證了行為（不是恆真斷言）？

輸出：依嚴重度排序的問題清單，每項附檔案:行號與具體修改建議。沒問題就明說。
"""
```

再建一個 `test-runner.toml`（把測試雜訊隔離在子代理）：

```toml
name = "test_runner"
description = "執行 dotnet test 並回報摘要。需要跑測試驗證時使用。"
developer_instructions = """
執行 `dotnet test`。全綠時只回報「N 個測試全部通過」。
有失敗時：列出失敗的測試名稱、斷言訊息、以及你判斷的可能原因，不要貼完整輸出。
"""
```

**欄位重點**：`name`、`description`、`developer_instructions` 必填（description 決定 agent 何時會主動委派給它）；`model_reasoning_effort`、`sandbox_mode` 選填，不填就繼承主 session。並行數量由 `agents.max_threads` 控制（預設 6）。

**驗證方式**：

- [ ] 修完一個 bug 後說「用 code_reviewer 審查我的變更」，或直接觀察 agent 會不會在適當時機自己委派。
- [ ] 用 test_runner 跑測試

---

## 5. Skills — 把重複流程做成一鍵指令

練習 2 每個 bug 都要走同一套流程，第二次開始你就會想把它做成指令。Codex 的 skill 是「一個資料夾 + `SKILL.md`」，專案層放在 **`.agents/skills/`**（注意：是 `.agents/`，不是 `.codex/`；個人全域的放 `~/.agents/skills/`）。

> 你可能在網路教學看到 `~/.codex/prompts/*.md` 的 custom prompts 寫法——官方文件已**不再收錄**這種做法，新流程請一律用 skills。

在 `training-repo/.agents/skills/fix-bug/SKILL.md` 建立：

```markdown
---
name: fix-bug
description: 依標準流程修復一個 bug：重現、定位、修復、回歸測試、commit。使用者明確要求修 bug 時才使用。
---

依照以下流程修復使用者描述的 bug（症狀在使用者的訊息裡）：

1. 先根據症狀推測涉及的頁面與流程，向使用者確認你對症狀的理解
2. 從 Controller 往下追到 Service、Repository，定位根因；
   說明根因後**等使用者確認**再動手修
3. 用最小變更修復，不要順手重構無關的程式碼
4. 使用code-reviewer來驗證改動
5. 補一個回歸測試（先確認它在修復前會失敗的邏輯），使用test-runner跑 `dotnet test` 確認全綠
6. 提示使用者回頁面實測，確認後以「症狀 → 根因 → 修法」格式撰寫 commit message 並 commit
```

之後輸入 `$fix-bug 訂單列表第一頁看不到新訂單`（或透過 `/skills` 選單挑選）就會啟動整套流程。

**格式重點**

- frontmatter 的 `name`、`description` 必填；`name` 建議用 kebab-case（全小寫、連字號）並與資料夾名稱保持一致
- Codex 也會**自動觸發** skill：任務內容符合 `description` 描述時它會自己選用——所以 description 要寫清楚「什麼時候該用、什麼時候不該用」，不想被自動觸發就在 description 明說「使用者明確要求時才使用」
- Skill 採「漸進載入」：平常只載入 name + description，被選用時才讀完整內容——所以主要說明寫在內文，不要塞在 description
- 資料夾裡可以放輔助腳本或參考文件，SKILL.md 內文用相對路徑引用

---

## 附：官方文件

- 設定總覽與 `config.toml` 參考：https://learn.chatgpt.com/docs/config-file/config-reference
- Approvals 與沙盒：https://learn.chatgpt.com/docs/agent-approvals-security
- Rules（execpolicy）：https://learn.chatgpt.com/docs/agent-configuration/rules
- Hooks：https://learn.chatgpt.com/docs/hooks
- Subagents：https://learn.chatgpt.com/docs/agent-configuration/subagents
- Skills：https://learn.chatgpt.com/docs/build-skills
