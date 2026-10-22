[CmdletBinding()]
param(
    [string]$DataDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot 'dist\Switchit\Switchit.exe'
$appArguments = @()
if ($DataDirectory) { $appArguments += @('--data-dir', $DataDirectory) }
if (Test-Path -LiteralPath $exePath -PathType Leaf) {
    & $exePath @appArguments
} else {
    & dotnet run --project (Join-Path $repoRoot 'Switchit\Switchit.csproj') -- @appArguments
    if ($LASTEXITCODE -ne 0) { throw "Switchit! exited with code $LASTEXITCODE." }
}
