[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$payload = Join-Path $root 'release-payload/v1.0.0.2-rc2'
$repo = 'Catacatata/ETDisplay'
$tag = 'v1.0.0.2-rc2'
$sourceCommit = 'dcf1441ae21308ea2e9fe23b5e69fd2b97b5b244'
$expected = [ordered]@{
    'ETDisplay-1.0.0.2-rc2.zip' = '9dd1da8fda954121548be6189e64eae3032e8bb827e138d8d6e387f59d73ccc9'
    'ETDisplay-1.0.0.2-rc2-source.zip' = '448439e31a2de787715f9865e7e5064669cf19d8c061ee1f10946adfe61880ae'
}
foreach ($name in $expected.Keys) {
    $path = Join-Path $payload $name
    if ((Get-Item $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse point rejected.' }
    if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected[$name]) { throw "Checksum mismatch: $name" }
}
$sumLines = @(Get-Content (Join-Path $payload 'SHA256SUMS.txt'))
if ($sumLines.Count -ne 2) { throw 'Unexpected checksum manifest.' }
foreach ($name in $expected.Keys) {
    if ($sumLines -notcontains ($expected[$name] + '  ' + $name)) { throw 'Checksum manifest differs.' }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($name in $expected.Keys) {
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $payload $name))
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -match '(^|/)\.\.(/|$)|^[\\/]|(^|/)(\.git|bin|obj|artifacts|\.vs|TestResults)/|verification-report\.json|\.trx$|\.pdb$|\.(ttf|ttc|otf)$') { throw 'Disallowed public ZIP entry.' }
        }
        if ($name -eq 'ETDisplay-1.0.0.2-rc2.zip') {
            foreach ($required in @('ETDisplay.dll','ETDisplay.Core.dll','ETDisplay.json','ETDisplay.deps.json','System.Speech.dll','runtimes/win/lib/net10.0/System.Speech.dll','LICENSE','THIRD-PARTY-NOTICES.md','licenses/System.Speech-LICENSE.txt','licenses/System.Speech-THIRD-PARTY-NOTICES.TXT','docs/INSTALL.md')) {
                if (!$zip.GetEntry($required)) { throw "Missing runtime entry: $required" }
            }
            $reader = [IO.StreamReader]::new($zip.GetEntry('ETDisplay.json').Open())
            try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            if ($manifest.InternalName -ne 'ETDisplay' -or $manifest.AssemblyVersion -ne '1.0.0.2' -or $manifest.DalamudApiLevel -ne 15) { throw 'Runtime manifest mismatch.' }
        } else {
            foreach ($required in @('ETDisplay/LICENSE','ETDisplay/ETDisplay/Plugin.cs','ETDisplay/ETDisplay/ETDisplay.csproj','ETDisplay/ETDisplay.Core/ETDisplay.Core.csproj','ETDisplay/ETDisplay.Core.Tests/ETDisplay.Core.Tests.csproj','ETDisplay/global.json')) {
                if (!$zip.GetEntry($required)) { throw "Missing corresponding source: $required" }
            }
        }
    } finally { $zip.Dispose() }
}
$index = @(Get-Content (Join-Path $root 'repo.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
$download = "https://github.com/$repo/releases/download/$tag/ETDisplay-1.0.0.2-rc2.zip"
if ($index.Count -ne 1 -or $index[0].AssemblyVersion -ne '1.0.0.2' -or $index[0].DalamudApiLevel -ne 15 -or $index[0].DownloadLinkInstall -ne $download -or $index[0].DownloadLinkUpdate -ne $download) { throw 'Repository index mismatch.' }
Write-Output 'PUBLIC_PAYLOAD_VALIDATED'
if ($ValidateOnly) { return }
if ($env:GITHUB_REPOSITORY -ne $repo -or !$env:GITHUB_TOKEN) { throw 'Publishing is only supported in the authorized repository Actions runner.' }
$headers = @{ Authorization = "Bearer $env:GITHUB_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'ETDisplay-Verified-Publisher' }
function Api([string]$Method, [string]$Path, $Body = $null) {
    $params = @{ Uri = "https://api.github.com/repos/$repo/$Path"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = ConvertTo-Json -InputObject $Body -Depth 8; $params.ContentType = 'application/json; charset=utf-8' }
    $response = Invoke-RestMethod @params
    # REST arrays are emitted as one pipeline object in PowerShell 7. Normalize explicitly,
    # especially an empty release/asset list under StrictMode.
    if ($response -is [Array]) {
        foreach ($item in $response) { Write-Output $item }
    } elseif ($null -ne $response) { Write-Output $response }
}
$tagRef = Api 'GET' "git/ref/tags/$tag"
$target = $tagRef.object
if ($target.type -eq 'tag') { $tagData = Api 'GET' "git/tags/$($target.sha)"; $target = $tagData.object }
if ($target.type -ne 'commit' -or $target.sha -ne $sourceCommit) { throw 'Release source tag moved; refusing publication.' }
$releases = @(Api 'GET' 'releases?per_page=100')
$matchingReleases = @($releases | Where-Object { $_.tag_name -eq $tag })
if ($matchingReleases.Count -gt 1) { throw 'Ambiguous release.' }
if ($matchingReleases.Count -eq 0) {
    $notes = Get-Content (Join-Path $payload 'RELEASE-NOTES.md') -Raw -Encoding UTF8
    $release = Api 'POST' 'releases' @{ tag_name = $tag; target_commitish = $sourceCommit; name = 'ETDisplay 1.0.0.2-rc2 - public test'; body = $notes; draft = $true; prerelease = $true }
} else { $release = $matchingReleases[0] }
if (!$release.prerelease) { throw 'Existing release is not marked as a pre-release; refusing to change it.' }
$files = @($expected.Keys) + @('SHA256SUMS.txt')
foreach ($name in $files) {
    $file = Join-Path $payload $name
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $assets = @(Api 'GET' "releases/$($release.id)/assets?per_page=100")
    $existing = @($assets | Where-Object { $_.name -eq $name })
    if ($existing.Count -gt 1) { throw 'Ambiguous asset.' }
    if ($existing.Count -eq 1) {
        if ($existing[0].digest -ne "sha256:$hash") { throw "Existing asset differs: $name (will not overwrite)." }
    } else {
        $type = if ($name.EndsWith('.zip')) { 'application/zip' } else { 'text/plain' }
        $uri = "https://uploads.github.com/repos/$repo/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($name))"
        $asset = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -InFile $file -ContentType $type
        if ($asset.digest -ne "sha256:$hash" -or $asset.size -ne (Get-Item $file).Length) { throw 'Uploaded asset verification failed.' }
    }
}
$assets = @(Api 'GET' "releases/$($release.id)/assets?per_page=100")
foreach ($name in $files) {
    $asset = @($assets | Where-Object { $_.name -eq $name })
    $hash = (Get-FileHash (Join-Path $payload $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.Count -ne 1 -or $asset[0].digest -ne "sha256:$hash") { throw 'Final release asset validation failed.' }
}
if ($release.draft) { $release = Api 'PATCH' "releases/$($release.id)" @{ draft = $false; prerelease = $true } }
if ($release.draft -or !$release.prerelease) { throw 'Release state verification failed.' }
Write-Output "PUBLISHED_RELEASE=$($release.html_url)"
