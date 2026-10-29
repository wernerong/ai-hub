param(
    [Parameter(Mandatory)][string]$SourceDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$CompilerPath = $env:INNO_SETUP_COMPILER
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) { throw 'Use a new installer test directory.' }
New-Item -ItemType Directory -Path $root | Out-Null
$installation = Join-Path $root 'installed'
$registryBase = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$registration = Join-Path $registryBase 'AIHub.InstallerTest_is1'
$legacy = Join-Path $registryBase 'AIHub.InstallerTest.Legacy'
$startup = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ((Test-Path -LiteralPath $registration) -or (Test-Path -LiteralPath $legacy)) { throw 'An earlier installer test registration needs inspection.' }
$report = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Label) {
    if (-not $Condition) { throw $Label }
    $report.Add('PASS ' + $Label)
    Write-Output ('PASS ' + $Label)
}
function RunSetup([string]$Label, [int]$ExpectedExit = 0) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=desktopicon', ('/DIR="{0}"' -f $installation), ('/LOG="{0}"' -f (Join-Path $root ($Label + '.log'))))
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if ($ExpectedExit -eq 0) { Check ($process.ExitCode -eq 0) ($Label + ' finishes successfully') }
    else { Check ($process.ExitCode -ne 0) ($Label + ' refuses to replace a running installation') }
}
$policies = Get-ExecutionPolicy -List | ConvertTo-Json -Compress
& (Join-Path $PSScriptRoot '..\Build-Installer.ps1') -SourceDirectory $SourceDirectory -OutputDirectory $root -CompilerPath $CompilerPath -TestBuild
$setup = (Get-ChildItem -LiteralPath $root -Filter '*-Setup-Test.exe').FullName
$shell = New-Object -ComObject WScript.Shell
$shortcuts = @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) | ForEach-Object { Join-Path $_ 'AI Hub Installer Test.lnk' }
try {
    RunSetup 'clean-install'
    $exe = Join-Path $installation 'AIHub.exe'
    $expectedHash = (Get-FileHash -LiteralPath (Join-Path $SourceDirectory 'AIHub.exe')).Hash
    Check ((Get-FileHash -LiteralPath $exe).Hash -eq $expectedHash) 'Clean installation contains the exact validated app'
    $entry = Get-ItemProperty -LiteralPath $registration
    Check ($entry.InstallLocation.TrimEnd('\') -eq $installation -and $entry.UninstallString -like '*unins000.exe*') 'Installed apps registers a native per-user uninstaller'
    foreach ($shortcut in $shortcuts) {
        Check ((Test-Path -LiteralPath $shortcut) -and $shell.CreateShortcut($shortcut).TargetPath -eq $exe) ('Shortcut targets the installed app: ' + [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($shortcut)))
    }
    $mutex = [Threading.Mutex]::new($true, 'Local\AIHub.InstallerTest.Desktop')
    try { RunSetup 'running-app' 1 }
    finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
    Check ((Get-FileHash -LiteralPath $exe).Hash -eq $expectedHash) 'Running-app refusal preserves the installed executable'
    # Exercise migration from the old PowerShell registration at the same path.
    New-Item -Path $legacy -Force | Out-Null
    New-ItemProperty -LiteralPath $legacy -Name InstallLocation -Value $installation -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $installation 'README.txt') -Value 'old release fixture'
    Set-Content -LiteralPath (Join-Path $installation 'personal.txt') -Value 'keep this file'
    RunSetup 'upgrade'
    Check (-not (Test-Path -LiteralPath $legacy)) 'Upgrade removes the matching legacy registration'
    Check ((Get-FileHash -LiteralPath (Join-Path $installation 'README.txt')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $SourceDirectory 'README.txt')).Hash) 'Upgrade replaces older release files'
    Check ((Get-Content -LiteralPath (Join-Path $installation 'personal.txt') -Raw).Trim() -eq 'keep this file') 'Upgrade preserves unrelated files'
    New-ItemProperty -Path $startup -Name 'AIHub.InstallerTest' -Value ('"' + $exe + '"') -PropertyType String -Force | Out-Null
    $uninstall = Start-Process -FilePath (Join-Path $installation 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="{0}"' -f (Join-Path $root 'uninstall.log'))) -WindowStyle Hidden -PassThru -Wait
    Check ($uninstall.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $exe)) 'Native uninstall removes the app successfully'
    Check (-not (Test-Path -LiteralPath $registration)) 'Native uninstall removes Installed apps registration'
    Check (@($shortcuts | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0) 'Native uninstall removes its own shortcuts'
    Check ($null -eq (Get-Item -LiteralPath $startup).GetValue('AIHub.InstallerTest', $null)) 'Native uninstall removes its matching startup entry'
    Check ((Get-Content -LiteralPath (Join-Path $installation 'personal.txt') -Raw).Trim() -eq 'keep this file') 'Native uninstall preserves unrelated files'
    Check ((Get-ExecutionPolicy -List | ConvertTo-Json -Compress) -eq $policies) 'Install, upgrade and uninstall leave PowerShell execution policies unchanged'
} catch {
    $report.Add('FAIL ' + $_.Exception.Message)
    throw
} finally {
    $report | Set-Content -LiteralPath (Join-Path $root 'installer-validation.txt') -Encoding utf8
}
