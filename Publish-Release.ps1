param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [string]$NotesFile,
    [switch]$Publish,
    [switch]$MakePublic
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$headers = @{}
$credential = @{}
try {
    if ((git remote get-url origin) -ne 'https://github.com/wernerong/ai-hub.git') { throw 'Unexpected GitHub remote.' }
    if ((git branch --show-current) -ne 'main') { throw 'Release from main.' }
    if (git status --porcelain) { throw 'Commit the release changes before publishing.' }
    $project = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AIHub.csproj') -Raw)
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part stable version.' }
    $commit = git rev-parse HEAD
    $tag = 'v' + $version
    if (-not $NotesFile) { $NotesFile = Join-Path $PSScriptRoot ('releases\v' + $version + '.md') }
    $notes = Get-Content -LiteralPath $NotesFile -Raw
    & (Join-Path $PSScriptRoot 'Package.ps1') -OutputDirectory $OutputDirectory
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    $zip = Join-Path $output "AIHub-v$version-windows-x64.zip"
    $checksums = Join-Path $output 'SHA256SUMS.txt'
    $localAssets = @($zip, $checksums)
    $localInfo = @{}
    foreach ($path in $localAssets) { $localInfo[[IO.Path]::GetFileName($path)] = @{ Length = (Get-Item -LiteralPath $path).Length; Hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() } }
    git show-ref --verify --quiet ('refs/tags/' + $tag)
    if ($LASTEXITCODE -eq 0) {
        if ((git rev-parse ($tag + '^{}')) -ne $commit) { throw 'The version tag points to another commit.' }
    } else {
        git tag -a $tag -m "AI Hub v$version"
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the annotated release tag.' }
    }
    git push --atomic origin 'HEAD:refs/heads/main' ('refs/tags/' + $tag)
    if ($LASTEXITCODE -ne 0) { throw 'Atomic branch/tag push failed.' }
    $remote = @(git ls-remote origin 'refs/heads/main' ('refs/tags/' + $tag + '^{}'))
    if ($LASTEXITCODE -ne 0 -or $remote.Count -ne 2 -or @($remote | Where-Object { ($_ -split '\s+')[0] -ne $commit }).Count) { throw 'Remote branch/tag verification failed.' }
    $env:GIT_TERMINAL_PROMPT = '0'
    $credentialLines = "protocol=https`nhost=github.com`npath=wernerong/ai-hub.git`n`n" | git -c credential.interactive=false credential fill 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Git Credential Manager has no GitHub sign-in.' }
    foreach ($line in $credentialLines) { $pair = $line -split '=', 2; if ($pair.Count -eq 2) { $credential[$pair[0]] = $pair[1] } }
    if (-not $credential.ContainsKey('password')) { throw 'No GitHub credential is available.' }
    $headers = @{ Authorization = 'Bearer ' + $credential['password']; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'AIHub-Release' }
    $api = 'https://api.github.com/repos/wernerong/ai-hub'
    $releases = @(Invoke-RestMethod -Uri ($api + '/releases?per_page=100') -Headers $headers)
    $existing = @($releases | Where-Object tag_name -eq $tag)
    if ($existing.Count) { throw 'A release already exists for this version; inspect it instead of overwriting assets.' }
    $body = @{ tag_name = $tag; target_commitish = $commit; name = "AI Hub v$version"; body = $notes; draft = $true; prerelease = $false } | ConvertTo-Json
    $release = Invoke-RestMethod -Uri ($api + '/releases') -Method Post -Headers $headers -ContentType 'application/json' -Body $body
    foreach ($path in $localAssets) {
        $name = [IO.Path]::GetFileName($path)
        $upload = 'https://uploads.github.com/repos/wernerong/ai-hub/releases/' + $release.id + '/assets?name=' + [Uri]::EscapeDataString($name)
        $contentType = if ($name.EndsWith('.zip')) { 'application/zip' } else { 'text/plain' }
        $asset = Invoke-RestMethod -Uri $upload -Method Post -Headers $headers -ContentType $contentType -InFile $path
        if ($asset.size -ne $localInfo[$name].Length -or $asset.digest -ne ('sha256:' + $localInfo[$name].Hash)) { throw "Uploaded asset verification failed: $name" }
    }
    $release = Invoke-RestMethod -Uri ($api + '/releases/' + $release.id) -Headers $headers
    if ($release.assets.Count -ne $localAssets.Count -or -not $release.draft) { throw 'The draft asset set is incorrect.' }
    foreach ($asset in $release.assets) {
        if (-not $localInfo.ContainsKey($asset.name) -or $asset.size -ne $localInfo[$asset.name].Length -or $asset.digest -ne ('sha256:' + $localInfo[$asset.name].Hash)) { throw 'Draft digest verification failed.' }
    }
    if ($MakePublic) {
        $repo = Invoke-RestMethod -Uri $api -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"private":false}'
        if ($repo.private) { throw 'Repository visibility did not change.' }
    }
    if ($Publish) {
        $repo = Invoke-RestMethod -Uri $api -Headers $headers
        if ($repo.private) { throw 'Make the repository public before publishing the public update channel.' }
        # A private-to-public change can briefly leave the release write route
        # returning 404 even while the verified draft remains readable.
        $releaseUri = $api + '/releases/' + $release.id
        for ($attempt = 0; $attempt -lt 4; $attempt++) {
            try {
                $release = Invoke-RestMethod -Uri $releaseUri -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"prerelease":false,"make_latest":"true"}'
                break
            } catch {
                if (-not $MakePublic -or $attempt -eq 3 -or [int]$_.Exception.Response.StatusCode -ne 404) { throw }
                Start-Sleep -Seconds ([int][Math]::Pow(2, $attempt + 1))
            }
        }
        if ($release.draft -or $release.prerelease) { throw 'The public release was not published.' }
    }
    [pscustomobject]@{ Version = $version; Tag = $tag; Commit = $commit; ReleaseId = $release.id; ReleaseUrl = $release.html_url; Draft = $release.draft; Assets = $localInfo; GitHubActionsMinutesUsed = 0 } |
        ConvertTo-Json -Depth 5 | Tee-Object -FilePath (Join-Path $output 'release-record.json')
} finally {
    $headers.Clear(); $credential.Clear(); $credentialLines = $null
    Pop-Location
}
