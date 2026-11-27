# Puts the release signer's two secrets into its Cloudflare Worker (server\release-signer), on edupode's PC:
#   SIGNING_KEY      from %USERPROFILE%\.vi-release\release-key.xml (the .NET XML key, turned into a JWK)
#   DISCORD_WEBHOOK  from %USERPROFILE%\.vi-release\discord-webhook.txt
# Neither is printed or written anywhere else: each goes to `wrangler secret put` through its input.
# Once set, nobody can read them back (not even in the Cloudflare dashboard); run this again to replace them.
param([switch]$KeyOnly, [switch]$WebhookOnly)
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:USERPROFILE '.vi-release'
$worker = Join-Path $PSScriptRoot '..\server\release-signer'

function Put([string]$name, [string]$value) {
    Push-Location $worker
    try {
        $value | npx --yes wrangler secret put $name 2>&1 | Where-Object { $_ -notmatch 'Enter a secret value' } | Select-Object -Last 2 | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "wrangler secret put $name failed." }
    }
    finally { Pop-Location }
}

function B64Url([string]$b64) { return $b64.TrimEnd('=').Replace('+', '-').Replace('/', '_') }

if (-not $WebhookOnly) {
    $xmlPath = Join-Path $dir 'release-key.xml'
    if (-not (Test-Path $xmlPath)) { throw "No release key at $xmlPath." }
    [xml]$x = [IO.File]::ReadAllText($xmlPath)
    $k = $x.RSAKeyValue
    foreach ($f in 'Modulus', 'Exponent', 'D', 'P', 'Q', 'DP', 'DQ', 'InverseQ') { if (-not $k.$f) { throw "${xmlPath} has no ${f}: is it the private key?" } }
    $jwk = [ordered]@{
        kty = 'RSA'; alg = 'RS256'; ext = $true; key_ops = @('sign')
        n = B64Url $k.Modulus; e = B64Url $k.Exponent; d = B64Url $k.D
        p = B64Url $k.P; q = B64Url $k.Q; dp = B64Url $k.DP; dq = B64Url $k.DQ; qi = B64Url $k.InverseQ
    } | ConvertTo-Json -Compress
    Put 'SIGNING_KEY' $jwk
    'SIGNING_KEY set.'
}
if (-not $KeyOnly) {
    $hookPath = Join-Path $dir 'discord-webhook.txt'
    if (-not (Test-Path $hookPath)) { throw "No webhook at $hookPath." }
    $hook = [IO.File]::ReadAllText($hookPath).Trim()
    if ($hook -notmatch '^https://(discord|discordapp)\.com/api/webhooks/\d+/[\w-]+$') { throw "${hookPath} does not hold a Discord webhook URL." }
    Put 'DISCORD_WEBHOOK' $hook
    'DISCORD_WEBHOOK set.'
}
