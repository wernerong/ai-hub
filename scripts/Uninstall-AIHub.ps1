param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
try {
    $expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\AI Hub')).TrimEnd('\')
    $actual = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actual, $expected)) { throw 'Unexpected installation location. No files were removed.' }
    $native = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AIHub_is1' -ErrorAction SilentlyContinue
    $uninstaller = Join-Path $expected 'unins000.exe'
    if ($native -and $native.InstallLocation.TrimEnd('\') -eq $expected -and (Test-Path -LiteralPath $uninstaller)) {
        $arguments = if ($Quiet) { @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') } else { @('/NORESTART') }
        $window = if ($Quiet) { 'Hidden' } else { 'Normal' }
        $process = Start-Process -FilePath $uninstaller -ArgumentList $arguments -PassThru -Wait -WindowStyle $window
        exit $process.ExitCode
    }
    if (-not $Quiet -and [Windows.Forms.MessageBox]::Show('Uninstall AI Hub? Your AI apps and saved zoom settings will remain.', 'Uninstall AI Hub', 'YesNo', 'Question') -ne 'Yes') { exit 0 }
    $exe = Join-Path $expected 'AIHub.exe'
    $running = @(Get-Process AIHub -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
    foreach ($process in $running) { if ($process.MainWindowHandle -ne 0) { [void]$process.CloseMainWindow() } }
    foreach ($process in $running) { if (-not $process.WaitForExit(15000)) { throw 'Close AI Hub and try uninstalling again. No apps were terminated.' } }
    Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'AIHub' -ErrorAction SilentlyContinue
    $shell = New-Object -ComObject WScript.Shell
    foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
        $shortcut = Join-Path $folder 'AI Hub.lnk'
        if ((Test-Path -LiteralPath $shortcut) -and $shell.CreateShortcut($shortcut).TargetPath -eq $exe) { Remove-Item -LiteralPath $shortcut }
    }
    foreach ($name in @('AIHub.exe', 'README.txt', 'validation.txt', 'Install-AIHub.ps1', 'Uninstall-AIHub.ps1')) {
        $file = [IO.Path]::GetFullPath((Join-Path $expected $name))
        if ([IO.Path]::GetDirectoryName($file) -ne $expected) { throw 'Unexpected uninstall file path.' }
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    if (-not (Get-ChildItem -LiteralPath $expected -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $expected }
    Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AIHub' -ErrorAction SilentlyContinue
    if (-not $Quiet) { [void][Windows.Forms.MessageBox]::Show('AI Hub has been uninstalled. Your AI apps and saved settings are unchanged.', 'AI Hub', 'OK', 'Information') }
} catch {
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'AI Hub uninstall', 'OK', 'Error')
    exit 1
}
