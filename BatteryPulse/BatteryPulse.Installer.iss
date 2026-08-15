; ============================================================================
; 筆電狀態快顯 - Inno Setup 安裝腳本
; ============================================================================
; 使用方式：用 Inno Setup Compiler 開啟本檔案後按 Ctrl+F9 編譯。
; 本腳本會產生單一安裝檔，但安裝後會把溫度感測需要的 runtime 放到
; {app}\runtime\LibreHardwareMonitor，讓 CPU/GPU 溫度讀取維持正常。
;
; TODO 標記是日後可自行替換的欄位。主程式與版本已先代入目前專案版本。
; ============================================================================

; TODO: 若日後把專案放到其他位置，請改成腳本所在資料夾以外的來源路徑。
#define SourceDir "."
#ifndef CurrentBinDir
#define CurrentBinDir SourceDir + "\dist\current"
#endif
#define BrandingDir SourceDir + "\assets\branding"
#define InstallerAssetsDir SourceDir + "\assets\installer"
#define RuntimeDir SourceDir + "\runtime\LibreHardwareMonitor"
#define ReleaseDate "2026-08-13"
#define ReleaseStamp "20260813"
; TODO: 請替換成正式產品名稱；此名稱會同時用於安裝資料夾與捷徑。
#define MyAppName "筆電狀態快顯"
; TODO: 每次發佈新版時更新這兩個版本號。
#define MyAppVersion "2.2.2"
#define MyAppVersionInfo "2.2.2.0"
; TODO: 請替換成正式公司或作者名稱。
#define MyAppPublisher "彰化的驕傲 / 陳昀"
; TODO: 若主執行檔改名，請同步修改這裡。
#define MainExeName "BatteryPulse.TopBar.exe"
#define OutputName "BatteryPulse-Setup-v" + MyAppVersion + "-" + ReleaseStamp

[Setup]
; ---------------------------------------------------------------------------
; 基本產品資訊與安裝器外觀
; ---------------------------------------------------------------------------
AppId={{8E41E5A7-1E9A-4D02-B7EE-2C7A3A2E5B61}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
; TODO: 可填入官方網站；目前刻意留空。
AppPublisherURL=
AppSupportURL=
AppUpdatesURL=
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
AllowNoIcons=yes
PrivilegesRequired=admin
Uninstallable=yes
UninstallDisplayIcon={app}\BatteryPulse.ProgramLogo.ico
SetupLogging=yes

; 版本資訊會出現在安裝檔的檔案內容中。
VersionInfoVersion={#MyAppVersionInfo}
VersionInfoTextVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安裝程式
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersionInfo}
VersionInfoCopyright=Copyright (c) 2026 {#MyAppPublisher}

WizardStyle=modern
SetupIconFile={#BrandingDir}\BatteryPulse.ProgramLogo.ico
OutputDir=.\dist\current\installer
OutputBaseFilename={#OutputName}
Compression=lzma2
SolidCompression=yes
LZMAUseSeparateProcess=yes

; ---------------------------------------------------------------------------
; 安裝語言選擇
; ShowLanguageDialog=yes 會在歡迎頁之前顯示語言選擇對話框。
; ---------------------------------------------------------------------------
ShowLanguageDialog=yes
UsePreviousLanguage=no
LanguageDetectionMethod=none

; TODO: 準備圖片後，取消下面兩行的註解。
; WizardImageFile={#SourceDir}\installer\WizardImage.png
; WizardSmallImageFile={#SourceDir}\installer\WizardSmallImage.png
;
; 建議圖片尺寸：
; WizardImageFile：比例約 164:314，建議至少 202 x 386 px，以適應高 DPI。
; WizardSmallImageFile：正方形，建議至少 58 x 58 px；做成 147 x 147 px 會更穩妥。

[Languages]
; 四種語言都使用 Inno Setup 內建的標準按鈕、頁面與解除安裝翻譯。
Name: "zh_TW"; MessagesFile: "{#InstallerAssetsDir}\languages\ChineseTraditional.isl"; LicenseFile: "{#InstallerAssetsDir}\InstallerLicense.zh-TW.txt"
Name: "zh_CN"; MessagesFile: "{#InstallerAssetsDir}\languages\ChineseSimplified.isl"; LicenseFile: "{#InstallerAssetsDir}\InstallerLicense.zh-CN.txt"
Name: "en"; MessagesFile: "{#InstallerAssetsDir}\languages\Default.isl"; LicenseFile: "{#InstallerAssetsDir}\InstallerLicense.en.txt"
Name: "ja"; MessagesFile: "{#InstallerAssetsDir}\languages\Japanese.isl"; LicenseFile: "{#InstallerAssetsDir}\InstallerLicense.ja.txt"

[LangOptions]
; LanguageName 是語言選擇對話框中顯示的原生名稱。
zh_TW.LanguageName=繁體中文
zh_CN.LanguageName=简体中文
en.LanguageName=English
ja.LanguageName=日本語

[CustomMessages]
; ---------------------------------------------------------------------------
; 自訂文案集中在這裡。之後只改文字，不必修改 [Code] 邏輯。
; %n 代表換行；%n%n 代表空一行。
; ---------------------------------------------------------------------------

; 繁體中文
zh_TW.WelcomeHeading=歡迎來到「筆電狀態快顯」
zh_TW.WelcomeBody=我們會替你安放一個安靜而清楚的狀態入口。%n%n接下來將完成必要檔案、捷徑與啟動設定，讓電量、功率與溫度資訊在需要時隨時可見。
zh_TW.InstallStage1=正在整理安裝空間…
zh_TW.InstallStage2=正在安放每一個檔案…
zh_TW.InstallStage3=正在整理捷徑與啟動入口…
zh_TW.InstallStage4=正在完成最後確認…
zh_TW.FinishHeading=「筆電狀態快顯」已準備就緒
zh_TW.FinishBody=安裝已完成。你的桌面狀態入口現在可以開始工作。%n%n你可以立即開啟程式，或稍後從桌面與開始選單進入。
zh_TW.LaunchAfterInstall=立即開啟「筆電狀態快顯」
zh_TW.ShortcutGroup=建立捷徑
zh_TW.DesktopShortcut=建立桌面捷徑
zh_TW.StartMenuShortcut=建立開始選單捷徑

; 簡體中文
zh_CN.WelcomeHeading=欢迎来到“笔记本状态快显”
zh_CN.WelcomeBody=我们会为你安放一个安静而清晰的状态入口。%n%n接下来将完成必要文件、快捷方式与启动设置，让电量、功率与温度信息在需要时随时可见。
zh_CN.InstallStage1=正在整理安装空间…
zh_CN.InstallStage2=正在安放每一个文件…
zh_CN.InstallStage3=正在整理快捷方式与启动入口…
zh_CN.InstallStage4=正在完成最后确认…
zh_CN.FinishHeading=“笔记本状态快显”已准备就绪
zh_CN.FinishBody=安装已完成。你的桌面状态入口现在可以开始工作。%n%n你可以立即打开程序，也可以稍后从桌面与开始菜单进入。
zh_CN.LaunchAfterInstall=立即打开“笔记本状态快显”
zh_CN.ShortcutGroup=创建快捷方式
zh_CN.DesktopShortcut=创建桌面快捷方式
zh_CN.StartMenuShortcut=创建开始菜单快捷方式

; English
en.WelcomeHeading=Welcome to Notebook Status Popup
en.WelcomeBody=We will place a quiet, clear status entry on your desktop.%n%nThe setup will arrange the required files, shortcuts, and launch settings so your power and temperature details are ready when you need them.
en.InstallStage1=Preparing the space for installation…
en.InstallStage2=Placing each file in its place…
en.InstallStage3=Arranging shortcuts and entry points…
en.InstallStage4=Completing the final checks…
en.FinishHeading=Notebook Status Popup is ready
en.FinishBody=Installation is complete. Your desktop status entry is ready to work.%n%nYou can open the program now, or return to it later from the desktop or Start menu.
en.LaunchAfterInstall=Launch Notebook Status Popup now
en.ShortcutGroup=Create shortcuts
en.DesktopShortcut=Create a desktop shortcut
en.StartMenuShortcut=Create a Start menu shortcut

; 日本語
ja.WelcomeHeading=「ノートパソコン状態ポップアップ」へようこそ
ja.WelcomeBody=デスクトップに、静かで見やすい状態表示を設置します。%n%n必要なファイル、ショートカット、起動設定を整え、電源と温度の情報を必要なときに確認できるようにします。
ja.InstallStage1=インストールの場所を整えています…
ja.InstallStage2=ファイルを一つずつ配置しています…
ja.InstallStage3=ショートカットと起動入口を整えています…
ja.InstallStage4=最後の確認を行っています…
ja.FinishHeading=「ノートパソコン状態ポップアップ」の準備が整いました
ja.FinishBody=インストールが完了しました。デスクトップの状態表示をすぐに利用できます。%n%n今すぐ起動するか、後でデスクトップまたはスタートメニューから開いてください。
ja.LaunchAfterInstall=「ノートパソコン状態ポップアップ」を今すぐ起動
ja.ShortcutGroup=ショートカットを作成
ja.DesktopShortcut=デスクトップショートカットを作成
ja.StartMenuShortcut=スタートメニューにショートカットを作成

[Tasks]
; 開始選單捷徑預設勾選，桌面捷徑讓使用者自行決定。
Name: "startmenuicon"; Description: "{cm:StartMenuShortcut}"; GroupDescription: "{cm:ShortcutGroup}"
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ShortcutGroup}"; Flags: unchecked

[Files]
; 主程式與外部程式圖示。
Source: "{#CurrentBinDir}\{#MainExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BrandingDir}\BatteryPulse.ProgramLogo.ico"; DestDir: "{app}"; Flags: ignoreversion

; BatteryPulse 會從這個資料夾載入 LibreHardwareMonitorLib.dll 及其相依檔案，
; 用來讀取 CPU/GPU 溫度與硬體感測數值。整個資料夾一起安裝最可靠。
Source: "{#RuntimeDir}\*"; DestDir: "{app}\runtime\LibreHardwareMonitor"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; 桌面捷徑與開始選單捷徑依 [Tasks] 選項建立。
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MainExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\BatteryPulse.ProgramLogo.ico"; Tasks: desktopicon
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MainExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\BatteryPulse.ProgramLogo.ico"; Tasks: startmenuicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon

[Run]
; 完成頁的「立即執行」勾選框。安靜等待，不阻塞安裝器關閉。
Filename: "{app}\{#MainExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent

; ---------------------------------------------------------------------------
; 使用者資料保留策略
; ---------------------------------------------------------------------------
; 目前沒有 [UninstallDelete]，因此 %APPDATA%\BatteryPulse 下的設定與歷史資料
; 不會被解除安裝刪除。這是「保留使用者資料」的預設策略。
; 若未來要提供清除資料選項，建議另做解除安裝前的明確勾選，不要默默刪除。

[Code]
var
  InstallStage: Integer;

procedure SetInstallStage(Stage: Integer);
begin
  if Stage = InstallStage then
    Exit;

  InstallStage := Stage;
  case Stage of
    1: WizardForm.StatusLabel.Caption := ExpandConstant('{cm:InstallStage1}');
    2: WizardForm.StatusLabel.Caption := ExpandConstant('{cm:InstallStage2}');
    3: WizardForm.StatusLabel.Caption := ExpandConstant('{cm:InstallStage3}');
    4: WizardForm.StatusLabel.Caption := ExpandConstant('{cm:InstallStage4}');
  end;
end;

procedure InitializeWizard;
begin
  InstallStage := 0;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
  begin
    WizardForm.WelcomeLabel1.Caption := ExpandConstant('{cm:WelcomeHeading}');
    WizardForm.WelcomeLabel2.Caption := ExpandConstant('{cm:WelcomeBody}');
  end;

  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedHeadingLabel.Caption := ExpandConstant('{cm:FinishHeading}');
    WizardForm.FinishedLabel.Caption := ExpandConstant('{cm:FinishBody}');
  end;

  if CurPageID = wpInstalling then
    SetInstallStage(1);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    InstallStage := 0;
    SetInstallStage(1);
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  Ratio: Double;
  Stage: Integer;
begin
  if MaxProgress <= 0 then
    Exit;

  Ratio := CurProgress / MaxProgress;
  if Ratio < 0.25 then
    Stage := 1
  else if Ratio < 0.55 then
    Stage := 2
  else if Ratio < 0.85 then
    Stage := 3
  else
    Stage := 4;

  SetInstallStage(Stage);
end;
