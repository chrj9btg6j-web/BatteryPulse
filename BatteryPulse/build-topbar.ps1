param(
    [string]$OutputName = 'BatteryPulse.TopBar.exe'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$winbase = Join-Path $wpf 'WindowsBase.dll'
$pcore = Join-Path $wpf 'PresentationCore.dll'
$pframe = Join-Path $wpf 'PresentationFramework.dll'
$systemXaml = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "找不到 .NET Framework C# 編譯器：$csc"
}

Push-Location $root
try {
    & $csc /nologo /target:winexe /optimize+ /debug:pdbonly /langversion:Default `
        "/out:$OutputName" /main:BatteryPulse.TopBarProgram /win32manifest:BatteryPulse.app.manifest /win32icon:BatteryPulse.ProgramLogo.ico `
        /resource:BatteryPulse.ChargeLightning.png,BatteryPulse.ChargeLightning.png `
        /resource:BatteryPulse.PowerLightning.png,BatteryPulse.PowerLightning.png `
        /reference:System.dll /reference:System.Core.dll /reference:System.Management.dll `
        /reference:System.Windows.Forms.dll /reference:Microsoft.VisualBasic.dll `
        "/reference:$winbase" "/reference:$pcore" "/reference:$pframe" "/reference:$systemXaml" `
        BatteryPulse.cs BatteryWindow.Advanced.cs AdvancedDashboard.cs PerformanceReader.cs BatteryLimitController.cs TopStatusBarWindow.cs
    if ($LASTEXITCODE -ne 0) {
        throw "BatteryPulse.TopBar 編譯失敗，錯誤碼：$LASTEXITCODE"
    }
    Write-Output (Join-Path $root $OutputName)
}
finally {
    Pop-Location
}
