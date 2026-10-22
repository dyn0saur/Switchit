[CmdletBinding()]
param(
    [string]$DestinationDirectory = [Environment]::GetFolderPath('Desktop')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot 'dist\Switchit\Switchit.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw 'Build the app first: powershell -NoProfile -File scripts\Publish.ps1'
}
if (-not (Test-Path -LiteralPath $DestinationDirectory -PathType Container)) {
    throw "Shortcut destination does not exist: $DestinationDirectory"
}
$shortcutPath = Join-Path $DestinationDirectory 'Switchit!.lnk'
$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = Split-Path -Parent $exePath
    $shortcut.IconLocation = "$exePath,0"
    $shortcut.Description = 'Switchit! activity time tracker'
    $shortcut.Save()
    $legacyPath = Join-Path $DestinationDirectory 'WessClock.lnk'
    if (Test-Path -LiteralPath $legacyPath -PathType Leaf) {
        $legacy = $shell.CreateShortcut($legacyPath)
        try {
            if ($legacy.TargetPath -eq (Join-Path $repoRoot 'dist\WessClock\WessClock.exe')) {
                Remove-Item -LiteralPath $legacyPath
            }
        } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($legacy) }
    }
} finally {
    if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}
$koreaZone = [TimeZoneInfo]::FindSystemTimeZoneById('Korea Standard Time')
$koreaNow = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $koreaZone)
Add-Content -LiteralPath (Join-Path $repoRoot 'history.md') -Value ('{0:yyyy-MM-dd HH:mm:ss} KST | Switchit! shortcut created: {1}' -f $koreaNow, $shortcutPath) -Encoding UTF8
Write-Output "Shortcut: $shortcutPath"
