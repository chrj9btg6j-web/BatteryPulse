# BatteryPulse v2.2.1

## Release type

正式補漏版（patch release）。本版不新增產品功能，專注修正 G Helper 切換 GPU 模式時可能造成 BatteryPulse 關閉的穩定性問題。

## 修正內容

- 將硬體快照的 WMI、ACPI、LibreHardwareMonitor 與效能讀取分段隔離；單一感測器失敗不再中斷整個更新循環。
- 偵測獨立 GPU 暫時停用或重新枚舉時，清除不匹配的 GPU 數值，避免顯示待機或過期資料。
- G Helper 切換 GPU 模式期間不呼叫 LibreHardwareMonitor 的關閉流程，改用冷卻後重建感測器物件，降低驅動切換造成程式關閉的機率。
- 為背景更新、頂端狀態列回呼與未處理工作例外加入診斷記錄，方便排查而不影響主流程。

## 版本與資產

- 程式版本：`2.2.1`
- 類型：正式版，非 Beta
- 執行檔：`BatteryPulse.TopBar-v2.2.1-20260813.exe`
- 安裝包：`BatteryPulse-Setup-v2.2.1-20260813.exe`
- 更新檢查仍使用既有 GitHub Releases 路徑。

## 驗證

- [x] C# 執行檔編譯成功。
- [x] Inno Setup 6.7.3 安裝包編譯成功。
- [x] `git diff --check` 通過。
- [ ] 需在實體 Windows 電腦使用 G Helper 實際切換節能、標準、獨顯或混合模式完成驗收。
