param([string]$BuildDir = '')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BuildDir)) {
    $BuildDir = Join-Path $projectRoot 'dist\visual-test'
}
if (-not [IO.Path]::IsPathRooted($BuildDir)) {
    $BuildDir = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $BuildDir))
}
New-Item -ItemType Directory -Path $BuildDir -Force | Out-Null

$appPath = Join-Path $BuildDir 'BatteryPulse.TopBar.exe'
if (-not (Test-Path -LiteralPath $appPath)) {
    & (Join-Path $PSScriptRoot 'build-topbar.ps1') -OutputDir $BuildDir
    if ($LASTEXITCODE -ne 0) { throw 'Top bar build failed.' }
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$testPath = Join-Path $BuildDir 'TopBarVisualTests.exe'
& $csc /nologo /target:exe /out:$testPath `
    "/reference:$appPath" `
    "/reference:$(Join-Path $wpf 'WindowsBase.dll')" `
    "/reference:$(Join-Path $wpf 'PresentationCore.dll')" `
    "/reference:$(Join-Path $wpf 'PresentationFramework.dll')" `
    "/reference:$(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll')" `
    (Join-Path $projectRoot 'tests\TopBarVisualTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Top bar visual test compilation failed.' }

& $testPath
if ($LASTEXITCODE -ne 0) { throw 'Top bar visual test failed.' }
