param([string]$BuildDir = '')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BuildDir)) {
    $BuildDir = Join-Path $projectRoot 'dist\monitor-test'
}
New-Item -ItemType Directory -Path $BuildDir -Force | Out-Null
$appPath = Join-Path $BuildDir 'BatteryPulse.TopBar.exe'
if (-not (Test-Path -LiteralPath $appPath)) {
    & (Join-Path $PSScriptRoot 'build-topbar.ps1') -OutputDir $BuildDir
}
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$testPath = Join-Path $BuildDir 'TopBarMonitorTests.exe'
& $csc /nologo /target:exe "/out:$testPath" `
    "/win32manifest:$(Join-Path $projectRoot 'src\BatteryPulse.app.manifest')" `
    "/reference:$appPath" `
    "/reference:$(Join-Path $wpf 'WindowsBase.dll')" `
    "/reference:$(Join-Path $wpf 'PresentationCore.dll')" `
    "/reference:$(Join-Path $wpf 'PresentationFramework.dll')" `
    "/reference:$(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll')" `
    /reference:System.Windows.Forms.dll `
    (Join-Path $projectRoot 'tests\TopBarMonitorTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Monitor test compilation failed.' }
& $testPath
if ($LASTEXITCODE -ne 0) { throw 'Monitor placement test failed.' }
