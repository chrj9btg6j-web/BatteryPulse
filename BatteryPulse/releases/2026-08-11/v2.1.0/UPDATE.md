# BatteryPulse 更新紀錄

日期：2026-08-11

版本基準：v2.1.0

## 今日整理內容

- 將原始碼集中到 `src`。
- 將程式圖示、閃電圖與安裝器素材集中到 `assets`。
- 將 LibreHardwareMonitor 放到 `runtime`，並同步修正程式載入路徑。
- 讓程式在安裝版、舊資料夾、`dist\current` 與日期 release 位置都能探索到感測 runtime。
- 將建置腳本與 Inno Setup 工具集中到 `build`。
- 將目前可執行檔放到 `dist\current`，舊測試版放到 `archive\builds\2026-08-11`。
- 新增可重複使用的版本封裝腳本，產生日期與版本命名的交付資料夾。
- 重新整理 Git 忽略規則，避免本機建置產物污染版本紀錄。

## 今日相關檔案

### 程式與路徑

- `src\BatteryPulse.cs`
- `src\BatteryWindow.Advanced.cs`
- `src\AdvancedDashboard.cs`
- `src\BatteryLimitController.cs`
- `src\PerformanceReader.cs`
- `src\TopStatusBarWindow.cs`
- `src\UpdateService.cs`
- `src\BatteryPulse.app.manifest`

### 建置與交付

- `build\build-topbar.ps1`
- `build\package-release.ps1`
- `BatteryPulse.Installer.iss`
- `dist\current\BatteryPulse.TopBar.exe`
- `dist\current\BatteryPulse.TopBar.pdb`
- `releases\2026-08-11\v2.1.0\`

### 文件與素材

- `README.md`
- `docs\design-system\`
- `docs\updates\2026\2026-08-11.md`
- `assets\branding\`
- `assets\installer\`
- `runtime\LibreHardwareMonitor\`

## 驗證狀態

- [x] 重新編譯 `dist\current\BatteryPulse.TopBar.exe`
- [x] 編譯 Inno Setup 安裝檔
- [x] 執行 `package-release.ps1` 產生版本快照
- [ ] 在本機測試頂端狀態列、進階頁、溫度讀取與充電上限讀值
- [x] 將版本快照的 SHA-256 清單保存到 release 資料夾

備註：啟動冒煙測試遇到桌面上已有的 `BatteryPulse.TopBar` 單例程序，因此新路徑程序依設計立即結束；未強制關閉使用中的程序。建置與 Inno Setup 編譯均已成功，runtime 路徑則以靜態檔案存在性完成驗證。
