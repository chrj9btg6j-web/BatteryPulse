# BatteryPulse 2.2.2

## 開機啟動修正

- 使用者第一次成功開啟 BatteryPulse 或 BatteryPulse Top Bar 後，程式會自動加入目前使用者的 Windows 開機啟動項目。
- 啟動項目寫入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理員權限，也不會建立桌面捷徑。
- 使用者在設定中關閉開機啟動後，程式會記住這個選擇，不會在下次啟動時強制重新開啟。
- 測試執行檔可使用 `--no-startup` 或 `--test-instance` 避免修改正式啟動設定。

這是啟動設定的補漏更新，不改變既有電池、功率與溫度讀取方式。
