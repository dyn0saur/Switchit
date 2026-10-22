[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$CompilerPath,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $repoRoot 'dist\package\Switchit'
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'Publish.ps1') -SelfContained -Zip -OutputDirectory $payload
}
if (-not $CompilerPath) {
    $candidates = @(
        (Join-Path $repoRoot '.tools\InnoSetup\ISCC.exe'),
        (Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $CompilerPath) { $CompilerPath = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
}
if (-not $CompilerPath) { $CompilerPath = & (Join-Path $PSScriptRoot 'Get-InstallerCompiler.ps1') }
if (-not (Test-Path -LiteralPath (Join-Path $payload 'Switchit.exe'))) { throw 'Publish the app first.' }
$output = Join-Path $repoRoot 'dist\installer'
& $CompilerPath '/Q' ('/DAppVersion=' + $Version) ('/DPayloadDir=' + $payload) ('/DOutputDir=' + $output) (Join-Path $repoRoot 'installer\Switchit.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
$installer = Join-Path $output "Switchit-Setup-$Version-win-x64.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($installer + '.sha256', "$hash  $([IO.Path]::GetFileName($installer))`n", [Text.UTF8Encoding]::new($false))
$koreaZone = [TimeZoneInfo]::FindSystemTimeZoneById('Korea Standard Time')
$koreaNow = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $koreaZone)
Add-Content -LiteralPath (Join-Path $repoRoot 'history.md') -Value ('{0:yyyy-MM-dd HH:mm:ss} KST | Switchit! {1} EXE installer built with SHA256 checksum.' -f $koreaNow, $Version) -Encoding UTF8
Write-Output "Installer: $installer"
