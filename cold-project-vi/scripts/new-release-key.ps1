# Makes edupode's release signing key (RSA 3072), once.
#   The private key stays on this PC: %USERPROFILE%\.vi-release\release-key.xml (back it up; never commit it).
#   The public key is written into launcher\ReleaseKey.cs, which the launcher checks every update with.
# Run again only to replace a lost or leaked key (every launcher out there then needs one manual update).
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:USERPROFILE '.vi-release'
$private = Join-Path $dir 'release-key.xml'
if ((Test-Path $private) -and -not ($args -contains '-Force')) {
    Write-Host "A key is already there: $private (pass -Force to replace it)."
    exit 1
}
New-Item -ItemType Directory -Force $dir | Out-Null
$rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(3072)
[IO.File]::WriteAllText($private, $rsa.ToXmlString($true))
$public = $rsa.ToXmlString($false)
$cs = @"
// edupode's release key, public half: the launcher installs an update only when manifest.json is
// signed with the private half (scripts\new-release-key.ps1 made it; it never leaves edupode's PC).
namespace VIctoryLauncher
{
    static class ReleaseKey
    {
        public const string PublicXml = "$public";
    }
}
"@
$target = Join-Path $PSScriptRoot '..\launcher\ReleaseKey.cs'
[IO.File]::WriteAllText($target, $cs.Replace("`r`n", "`n"))
Write-Host "Private key: $private  (back it up somewhere safe)"
Write-Host "Public key written to launcher\ReleaseKey.cs"
