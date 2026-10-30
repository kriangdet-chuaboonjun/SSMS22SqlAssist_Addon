# Publishes dist\SqlAssistSetup-<version>.exe as GitHub release v<version>; the extension's update check reads it.
# The release text is that version's section of CHANGELOG.md ("## [x.y.z] - date"); the script stops if it is missing.
# Run .\build-setup.ps1 first, commit and push. Needs a token with "Contents: read and write" on the repository
# in $env:GITHUB_TOKEN (fine-grained PAT, or `gh auth token`).
# -UpdateNotes: rewrite the text of the existing release v<version> from CHANGELOG.md (nothing is rebuilt or uploaded);
# with -ForVersion 1.3.1 it does that for an older release.
param([switch]$Draft, [switch]$UpdateNotes, [string]$ForVersion)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$repo = "kriangdet-chuaboonjun/SSMS22SqlAssist_Addon"   # keep in sync with ReleaseInfo.Repository
if ($ForVersion -and -not $UpdateNotes) { throw "-ForVersion only goes with -UpdateNotes." }
$version = if ($ForVersion) { $ForVersion } else { ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version }
$tag = "v$version"
$exe = Join-Path $root "dist\SqlAssistSetup-$version.exe"

if (-not $env:GITHUB_TOKEN) { throw "Set `$env:GITHUB_TOKEN first." }

$headers = @{
    Authorization          = "Bearer $env:GITHUB_TOKEN"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}
$api = "https://api.github.com/repos/$repo"

# Release text: the CHANGELOG.md sections ("## [x.y.z] - date" up to the next "## [") of this version and of any
# version since the previous published release that never got a release of its own (e.g. 1.3.2 inside v1.3.3).
$changelog = Get-Content (Join-Path $root "CHANGELOG.md") -Raw -Encoding UTF8
$sections = [regex]::Matches($changelog, "(?ms)^## \[(\d+\.\d+\.\d+)\]([^\r\n]*)\r?\n(.*?)(?=^## \[|\z)")
if (-not ($sections | Where-Object { $_.Groups[1].Value -eq $version -and $_.Groups[3].Value.Trim() })) {
    throw "CHANGELOG.md has no section for [$version] - add one first."
}
# Parenthesised so the JSON array is unrolled: PowerShell 7 pipes Invoke-RestMethod's array as one object.
$previous = (Invoke-RestMethod "$api/releases?per_page=100" -Headers $headers) |
    Where-Object { -not $_.draft -and $_.tag_name -match '^v?\d+\.\d+\.\d+$' } |
    ForEach-Object { [version]($_.tag_name.TrimStart('v')) } |
    Where-Object { $_ -lt [version]$version } | Sort-Object -Descending | Select-Object -First 1
$included = @($sections | Where-Object {
    $v = [version]$_.Groups[1].Value
    $v -le [version]$version -and (-not $previous -or $v -gt $previous) -and $_.Groups[3].Value.Trim()
})
$notes = if ($included.Count -eq 1) { $included[0].Groups[3].Value.Trim() } else {
    ($included | ForEach-Object { "## " + $_.Groups[1].Value + $_.Groups[2].Value + "`n`n" + $_.Groups[3].Value.Trim() }) -join "`n`n"
}
# UTF-8 bytes: Windows PowerShell 5.1 would otherwise send the Thai text in the wrong encoding.
function Send-Json($uri, $method, $object) {
    $json = $object | ConvertTo-Json
    Invoke-RestMethod $uri -Method $method -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($json)) -ContentType "application/json; charset=utf-8"
}

if ($UpdateNotes) {
    $existing = Invoke-RestMethod "$api/releases/tags/$tag" -Headers $headers
    # Keep GitHub's "**Full Changelog**: compare link" line under the new text.
    $compare = ([string]$existing.body -split "\r?\n" | Where-Object { $_ -like '`*`*Full Changelog`*`**' } | Select-Object -First 1)
    $updated = Send-Json "$api/releases/$($existing.id)" Patch @{ body = $notes + $(if ($compare) { "`n`n" + $compare } else { "" }) }
    Write-Host "Updated notes of $($updated.html_url)" -ForegroundColor Green
    return
}

if (-not (Test-Path $exe)) { throw "$exe not found - run .\build-setup.ps1 first." }
if (git -C $root status --porcelain) { throw "Working tree has uncommitted changes." }
$head = (git -C $root rev-parse HEAD).Trim()
git -C $root fetch origin --quiet
if (-not (git -C $root branch -r --contains $head)) { throw "HEAD $head is not pushed to origin." }

$existing = $null
try { $existing = Invoke-RestMethod "$api/releases/tags/$tag" -Headers $headers } catch { }
if ($existing) { throw "Release $tag already exists: $($existing.html_url)" }

# GitHub creates the tag on HEAD if it doesn't exist yet. The CHANGELOG section comes first; GitHub appends its
# generated notes (the compare link to the previous release).
$release = Send-Json "$api/releases" Post @{
    tag_name               = $tag
    target_commitish       = $head
    name                   = "SqlAssist $version"
    body                   = $notes
    draft                  = [bool]$Draft
    generate_release_notes = $true
}

$name = Split-Path $exe -Leaf
$upload = $release.upload_url -replace "\{.*\}$", "?name=$name"
$asset = Invoke-RestMethod $upload -Method Post -Headers $headers -InFile $exe -ContentType "application/octet-stream"

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($asset.digest -and $asset.digest -ne "sha256:$hash") { throw "Uploaded asset digest $($asset.digest) does not match $hash." }
Write-Host "Published $($release.html_url)" -ForegroundColor Green
Write-Host "  $name  sha256:$hash"
