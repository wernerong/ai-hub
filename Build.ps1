param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))

$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot 'AIHub.csproj') -c Release -r win-x64 --self-contained true -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'AI Hub publish failed.' }
Copy-Item (Join-Path $PSScriptRoot 'README.txt') $OutputDirectory -Force
Copy-Item (Join-Path $PSScriptRoot 'scripts\Uninstall-AIHub.ps1') $OutputDirectory -Force
Copy-Item (Join-Path $PSScriptRoot 'scripts\Install-AIHub.ps1') $OutputDirectory -Force
Write-Output (Join-Path $OutputDirectory 'AIHub.exe')
