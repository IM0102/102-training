---
name: fix-bug
description: 依標準流程修復一個 bug：重現、定位、修復、回歸測試、commit
disable-model-invocation: true
---

依照以下流程修復使用者描述的 bug（症狀：$ARGUMENTS）：

1. 先根據症狀推測涉及的頁面與流程，向使用者確認你對症狀的理解
2. 從 Controller 往下追到 Service、Repository，定位根因；
   說明根因後**等使用者確認**再動手修
3. 用最小變更修復，不要順手重構無關的程式碼
4. 使用code-reviewer來驗證改動
5. 補一個回歸測試（先確認它在修復前會失敗的邏輯），使用test-runner跑 `dotnet test` 確認全綠
6. 提示使用者回頁面實測，確認後以「症狀 → 根因 → 修法」格式撰寫 commit message 並 commit
