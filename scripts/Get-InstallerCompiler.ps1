[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolDirectory = Join-Path $repoRoot '.tools\InnoSetup'
$compiler = Join-Path $toolDirectory 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { return $compiler }
$downloads = Join-Path $repoRoot '.tools\downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
$file = Join-Path $downloads 'innosetup-7.1.0-x64.exe'
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe'
Invoke-WebRequest -Uri $url -OutFile $file
Invoke-WebRequest -Uri ($url + '.issig') -OutFile ($file + '.issig')
$hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
$expectedHash = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'
if ($hash -ne $expectedHash -or (Get-Content -LiteralPath ($file + '.issig') -Raw) -notmatch [regex]::Escape($hash)) {
    throw 'Official Inno Setup download hash did not match.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $file
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B.V.') {
    throw 'Inno Setup Authenticode validation failed.'
}
$setup = Start-Process -FilePath $file -ArgumentList @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS', ('/DIR="' + $toolDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
if ($setup.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'Inno Setup compiler installation failed.' }
return $compiler
