param(
    [string]$Version = '2.2.2.1',
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$InstallerPath = '',
    [string]$UpdateNotePath = '',
    [string]$BuildDir = ''
)

$ErrorActionPreference = 'Stop'

# Create an immutable, date-stamped release snapshot from dist/current.
$projectRoot = Split-Path -Parent $PSScriptRoot
$currentDir = $BuildDir
if ([string]::IsNullOrWhiteSpace($currentDir)) {
    $currentDir = Join-Path $projectRoot 'dist\current'
} elseif (-not [System.IO.Path]::IsPathRooted($currentDir)) {
    $currentDir = Join-Path $projectRoot $currentDir
}
$releaseRoot = Join-Path $projectRoot ("releases\{0}\v{1}" -f $Date, $Version)
$binDir = Join-Path $releaseRoot 'bin'
$installerDir = Join-Path $releaseRoot 'installer'
$symbolsDir = Join-Path $releaseRoot 'symbols'
$stamp = $Date.Replace('-', '')

if (-not (Test-Path -LiteralPath $currentDir)) {
    throw "The current build directory was not found: $currentDir"
}

$currentExe = Join-Path $currentDir 'BatteryPulse.TopBar.exe'
if (-not (Test-Path -LiteralPath $currentExe)) {
    throw "Build the current executable before packaging: $currentExe"
}

if ((Test-Path -LiteralPath $releaseRoot) -and
    ((Get-ChildItem -LiteralPath $releaseRoot -File -Recurse | Measure-Object).Count -gt 0)) {
    throw "Release folder already contains files; choose a new version or date: $releaseRoot"
}

New-Item -ItemType Directory -Path $binDir, $installerDir, $symbolsDir -Force | Out-Null

$releaseExe = Join-Path $binDir ("BatteryPulse.TopBar-v{0}-{1}.exe" -f $Version, $stamp)
Copy-Item -LiteralPath $currentExe -Destination $releaseExe

$currentPdb = Join-Path $currentDir 'BatteryPulse.TopBar.pdb'
if (Test-Path -LiteralPath $currentPdb) {
    Copy-Item -LiteralPath $currentPdb -Destination (Join-Path $symbolsDir ("BatteryPulse.TopBar-v{0}-{1}.pdb" -f $Version, $stamp))
}

if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
    if (-not (Test-Path -LiteralPath $InstallerPath)) {
        throw "Installer file was not found: $InstallerPath"
    }
    $installerName = "BatteryPulse-Setup-v{0}-{1}.exe" -f $Version, $stamp
    Copy-Item -LiteralPath $InstallerPath -Destination (Join-Path $installerDir $installerName)
}

$updatePath = $UpdateNotePath
if ([string]::IsNullOrWhiteSpace($updatePath)) {
    $updatePath = Join-Path $projectRoot ("docs\updates\{0}\{1}.md" -f $Date.Substring(0, 4), $Date)
} elseif (-not [System.IO.Path]::IsPathRooted($updatePath)) {
    $updatePath = Join-Path $projectRoot $updatePath
}
if (Test-Path -LiteralPath $updatePath) {
    Copy-Item -LiteralPath $updatePath -Destination (Join-Path $releaseRoot 'UPDATE.md')
}

$files = Get-ChildItem -LiteralPath $releaseRoot -File -Recurse | Where-Object { $_.Name -ne 'SHA256SUMS.txt' }
$hashLines = foreach ($file in $files) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $relative = $file.FullName.Substring($releaseRoot.Length).TrimStart('\')
    "$hash  $relative"
}
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Value $hashLines -Encoding UTF8

Write-Output $releaseRoot
