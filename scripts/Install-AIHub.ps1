param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$installation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\AI Hub')).TrimEnd('\')
$names = @('README.txt', 'validation.txt', 'Install-AIHub.ps1', 'Uninstall-AIHub.ps1', 'AIHub.exe')
foreach ($name in $names) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "The release is incomplete: $name" }
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'AIHub.exe')).ProductVersion.Split('+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The release version is invalid.' }
$exe = Join-Path $installation 'AIHub.exe'
if (-not [StringComparer]::OrdinalIgnoreCase.Equals($source, $installation)) {
    $running = @(Get-Process AIHub -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
    foreach ($process in $running) { if ($process.MainWindowHandle -ne 0) { [void]$process.CloseMainWindow() } }
    foreach ($process in $running) {
        if (-not $process.WaitForExit(15000)) { throw 'Close AI Hub and retry. No AI apps were terminated.' }
    }
    New-Item -ItemType Directory -Path $installation -Force | Out-Null
    if ((Get-Item -LiteralPath $installation).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The installation folder must not be redirected.' }
    $backup = Join-Path (Join-Path $env:LOCALAPPDATA 'AIHub\install-backups') ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $existed = @{}
    $replaced = [Collections.Generic.List[string]]::new()
    try {
        foreach ($name in $names) {
            $target = Join-Path $installation $name
            if (Test-Path -LiteralPath $target) {
                if ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'An installation file is redirected.' }
                Copy-Item -LiteralPath $target -Destination (Join-Path $backup $name)
                $existed[$name] = $true
            }
            Copy-Item -LiteralPath (Join-Path $source $name) -Destination ($target + '.installing') -Force
        }
        foreach ($name in $names) {
            Move-Item -LiteralPath ((Join-Path $installation $name) + '.installing') -Destination (Join-Path $installation $name) -Force
            $replaced.Add($name)
        }
    } catch {
        foreach ($name in $replaced) {
            $target = Join-Path $installation $name
            if ($existed.ContainsKey($name)) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination $target -Force }
            else { Remove-Item -LiteralPath $target -ErrorAction SilentlyContinue }
        }
        throw
    } finally {
        foreach ($name in $names) { Remove-Item -LiteralPath ((Join-Path $installation $name) + '.installing') -ErrorAction SilentlyContinue }
    }
}
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'AI Hub.lnk'))
    $shortcut.TargetPath = $exe; $shortcut.WorkingDirectory = $installation
    $shortcut.Description = 'AI Hub - AI apps and usage in one workspace'; $shortcut.IconLocation = $exe + ',0'; $shortcut.Save()
}
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AIHub'
$nativeKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AIHub_is1'
$native = Get-ItemProperty -LiteralPath $nativeKey -ErrorAction SilentlyContinue
$keepNative = $native -and $native.InstallLocation.TrimEnd('\') -eq $installation -and (Test-Path -LiteralPath (Join-Path $installation 'unins000.exe'))
if ($keepNative) { $key = $nativeKey }
if (-not (Test-Path -LiteralPath $key)) { New-Item -Path $key -Force | Out-Null }
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$uninstall = '"' + $powershell + '" -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installation 'Uninstall-AIHub.ps1') + '"'
$values = @{ DisplayName = 'AI Hub'; DisplayVersion = $version; Publisher = 'wernerong'; InstallLocation = $installation;
    DisplayIcon = $exe; UninstallString = $uninstall; QuietUninstallString = $uninstall + ' -Quiet'; URLInfoAbout = 'https://github.com/wernerong/ai-hub' }
if ($keepNative) { $values.Remove('UninstallString'); $values.Remove('QuietUninstallString') }
foreach ($entry in $values.GetEnumerator()) { New-ItemProperty -LiteralPath $key -Name $entry.Key -Value $entry.Value -PropertyType String -Force | Out-Null }
New-ItemProperty -LiteralPath $key -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -LiteralPath $key -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -LiteralPath $startupKey -Name AIHub -ErrorAction SilentlyContinue) { Set-ItemProperty -LiteralPath $startupKey -Name AIHub -Value ('"' + $exe + '"') }
Write-Output "Installed AI Hub v$version in $installation."
if (-not $NoLaunch) { Start-Process -FilePath $exe -WorkingDirectory $installation -WindowStyle Normal }
