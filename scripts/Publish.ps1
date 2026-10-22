[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$Zip,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'Switchit\Switchit.csproj'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'dist\Switchit' }
$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }

# Only generated payload files live here. A clean publish avoids packaging obsolete assembly names.
if (Test-Path -LiteralPath $outputPath) {
    $resolvedOutput = [IO.Path]::GetFullPath($outputPath)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist')).TrimEnd('\') + '\'
    if (-not $resolvedOutput.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid publish output path.' }
    $running = Get-Process Switchit -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $resolvedOutput 'Switchit.exe') }
    if ($running) { throw 'Close the running payload before publishing. Current records must be saved first.' }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

& dotnet publish $projectPath -c Release -r win-x64 --self-contained $selfContainedValue -o $outputPath '-p:RuntimeFrameworkVersion=9.0.21' '-p:DebugType=None' '-p:DebugSymbols=false'
if ($LASTEXITCODE -ne 0) { throw "Switchit! publish failed with exit code $LASTEXITCODE." }

$exePath = Join-Path $outputPath 'Switchit.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { throw 'Switchit.exe was not produced.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $outputPath 'README.md') -Force

if ($Zip) {
    $packageName = if ($SelfContained) { 'Switchit-win-x64-self-contained.zip' } else { 'Switchit-win-x64.zip' }
    $zipPath = Join-Path (Join-Path $repoRoot 'dist') $packageName
    Compress-Archive -LiteralPath $outputPath -DestinationPath $zipPath -Force
    Write-Output "Package: $zipPath"
}

$koreaZone = [TimeZoneInfo]::FindSystemTimeZoneById('Korea Standard Time')
$koreaNow = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $koreaZone)
$historyLine = '{0:yyyy-MM-dd HH:mm:ss} KST | Switchit! Windows x64 Release published (self-contained={1}).' -f $koreaNow, $selfContainedValue
Add-Content -LiteralPath (Join-Path $repoRoot 'history.md') -Value $historyLine -Encoding UTF8
Write-Output "Ready: $exePath"
