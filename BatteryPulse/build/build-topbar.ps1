param(
    [string]$OutputName = 'BatteryPulse.TopBar.exe',
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'

# The script lives in BatteryPulse/build; all project paths resolve from the
# project root so the source tree can be reorganized without breaking builds.
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src'
$brandingRoot = Join-Path $projectRoot 'assets\branding'

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $projectRoot 'dist\current'
}

if ([IO.Path]::IsPathRooted($OutputName)) {
    $outputPath = $OutputName
} else {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    $outputPath = Join-Path $OutputDir $OutputName
}

$outputDirectory = Split-Path -Parent $outputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$drawing = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$winbase = Join-Path $wpf 'WindowsBase.dll'
$pcore = Join-Path $wpf 'PresentationCore.dll'
$pframe = Join-Path $wpf 'PresentationFramework.dll'
$systemXaml = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll'
$manifest = Join-Path $sourceRoot 'BatteryPulse.app.manifest'
$icon = Join-Path $brandingRoot 'BatteryPulse.ProgramLogo.ico'
$chargeIcon = Join-Path $brandingRoot 'BatteryPulse.ChargeLightning.png'
$powerIcon = Join-Path $brandingRoot 'BatteryPulse.PowerLightning.png'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "The .NET Framework C# compiler was not found: $csc"
}

& $csc /nologo /target:winexe /optimize+ /debug:pdbonly /langversion:Default `
    "/out:$outputPath" /main:BatteryPulse.TopBarProgram "/win32manifest:$manifest" "/win32icon:$icon" `
    "/resource:$chargeIcon,BatteryPulse.ChargeLightning.png" `
    "/resource:$powerIcon,BatteryPulse.PowerLightning.png" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Management.dll `
    "/reference:$drawing" /reference:System.Windows.Forms.dll /reference:Microsoft.VisualBasic.dll `
    "/reference:$winbase" "/reference:$pcore" "/reference:$pframe" "/reference:$systemXaml" `
    (Join-Path $sourceRoot 'BatteryPulse.cs') `
    (Join-Path $sourceRoot 'BatteryWindow.Advanced.cs') `
    (Join-Path $sourceRoot 'AdvancedDashboard.cs') `
    (Join-Path $sourceRoot 'PerformanceReader.cs') `
    (Join-Path $sourceRoot 'BatteryLimitController.cs') `
    (Join-Path $sourceRoot 'UpdateService.cs') `
    (Join-Path $sourceRoot 'TopBarTrayIcon.cs') `
    (Join-Path $sourceRoot 'TopStatusBarWindow.cs')

if ($LASTEXITCODE -ne 0) {
    throw "BatteryPulse.TopBar compilation failed with exit code $LASTEXITCODE"
}

Write-Output $outputPath
