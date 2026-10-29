param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'))

$ErrorActionPreference = 'Stop'
$project = [xml](Get-Content (Join-Path $PSScriptRoot 'AIHub.csproj') -Raw)
$version = $project.Project.PropertyGroup.Version
$output = [IO.Path]::GetFullPath($OutputDirectory)
$staging = Join-Path $output "AIHub-v$version-windows-x64"
if (Test-Path -LiteralPath $staging) { throw "Package staging directory already exists: $staging" }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $staging
$exe = Join-Path $staging 'AIHub.exe'
$validation = Join-Path $output "validation-$version"
$test = Start-Process -FilePath $exe -ArgumentList @('--self-test', ('"{0}"' -f $validation)) -PassThru -WindowStyle Hidden
if (-not $test.WaitForExit(60000)) { throw 'Self-test exceeded 60 seconds; inspect the test process before packaging.' }
$report = Join-Path $validation 'validation.txt'
if ($test.ExitCode -ne 0 -or -not (Test-Path $report) -or (Select-String -Path $report -Pattern '^FAIL' -Quiet)) { throw 'AI Hub self-test failed.' }
Copy-Item $report $staging
$zip = Join-Path $output "AIHub-v$version-windows-x64.zip"
Compress-Archive -Path $exe,(Join-Path $staging 'README.txt'),(Join-Path $staging 'validation.txt'),(Join-Path $staging 'Install-AIHub.ps1'),(Join-Path $staging 'Uninstall-AIHub.ps1') -DestinationPath $zip -Force
& (Join-Path $PSScriptRoot 'Build-Installer.ps1') -SourceDirectory $staging -OutputDirectory $output
$setup = Join-Path $output "AIHub-v$version-windows-x64-Setup.exe"
@($setup, $zip) | ForEach-Object {
    $checksum = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    "$checksum  $([IO.Path]::GetFileName($_))"
} | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Output $zip
