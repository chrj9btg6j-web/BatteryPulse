param(
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if ($OutputDir) {
        & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -OutputDir $OutputDir
    } else {
        & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    }
    if ($LASTEXITCODE -ne 0) { throw "Codex usage tests failed in Windows PowerShell" }
    return
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src'
$testsRoot = Join-Path $projectRoot 'tests'

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path (Join-Path $projectRoot 'work') 'test-codex-usage'
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "The .NET Framework C# compiler was not found: $csc"
}

$outputPath = Join-Path $OutputDir 'CodexUsageReaderTests.dll'
$sourcePath = Join-Path $sourceRoot 'CodexUsageReader.cs'
$testPath = Join-Path $testsRoot 'CodexUsageReaderTests.cs'
$systemWebExtensions = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll'

& $csc /nologo /target:library /optimize+ "/out:$outputPath" "/reference:$systemWebExtensions" $sourcePath $testPath
if ($LASTEXITCODE -ne 0) {
    throw "CodexUsageReader test compilation failed with exit code $LASTEXITCODE"
}

try {
    $testAssembly = [Reflection.Assembly]::LoadFrom($outputPath)
} catch {
    throw "Codex usage test assembly could not be loaded: $($_.Exception.Message)"
}
if ($null -eq $testAssembly) { throw 'Codex usage test assembly could not be loaded' }
$testType = $testAssembly.GetType('CodexUsageReaderTests', $true)
$testEntry = $testType.GetMethod('Main', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic)
$testExitCode = [int]$testEntry.Invoke($null, @())
if ($testExitCode -ne 0) {
    throw "CodexUsageReader tests failed with exit code $testExitCode"
}

Write-Output $outputPath
