param(
    [Parameter(Mandatory)][string]$SourceDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$CompilerPath = $env:INNO_SETUP_COMPILER,
    [switch]$TestBuild
)
$ErrorActionPreference = 'Stop'
if (-not $CompilerPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $candidates = @($command.Source, "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
    $CompilerPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Install Inno Setup 6.7 or newer, or set INNO_SETUP_COMPILER to its ISCC.exe. This is a build-time tool only.'
}
$source = [IO.Path]::GetFullPath($SourceDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($name in @('AIHub.exe', 'README.txt', 'validation.txt', 'Install-AIHub.ps1', 'Uninstall-AIHub.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "Incomplete installer payload: $name" }
}
$project = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AIHub.csproj') -Raw)
$version = [string]$project.Project.PropertyGroup.Version
$actual = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'AIHub.exe')).ProductVersion.Split('+')[0]
if ($actual -ne $version) { throw 'The installer payload version does not match the project.' }
$name = "AIHub-v$version-windows-x64-Setup"
if ($TestBuild) { $name += '-Test' }
$arguments = @('/Qp', "/DAppVersion=$version", "/DAppSource=$source", "/DBuildOutput=$output", "/DSetupName=$name")
if ($TestBuild) { $arguments += '/DInstallerTest=1' }
& $CompilerPath @arguments (Join-Path $PSScriptRoot 'installer\AIHub.iss')
if ($LASTEXITCODE -ne 0) { throw 'The native Windows installer build failed.' }
$setup = Join-Path $output ($name + '.exe')
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'The compiler did not produce the expected installer.' }
Write-Output $setup
