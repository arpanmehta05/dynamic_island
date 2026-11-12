# Removes Dynamic Island: stops it, removes start-with-Windows, the Start menu entry, the Settings > Apps entry and the program folder.
# Settings > Apps runs this. It copies itself to a temp folder first, because it deletes the folder it lives in.
param([switch]$Quiet, [switch]$Relaunched)

$ErrorActionPreference = 'Continue'
$dir    = Join-Path $env:LOCALAPPDATA 'Programs\DynamicIsland'
$run    = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninst = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DynamicIsland'
$menu   = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Dynamic Island.lnk'

if (-not $Relaunched) {
    $copy = Join-Path $env:TEMP 'DynamicIsland-uninstall.ps1'
    Copy-Item $PSCommandPath $copy -Force
    $args2 = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$copy`"", '-Relaunched')
    if ($Quiet) { $args2 += '-Quiet' }
    Start-Process powershell.exe -ArgumentList $args2 -WindowStyle Hidden
    exit 0
}

Get-Process DynamicIsland -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Remove-ItemProperty -Path $run -Name 'DynamicIsland' -ErrorAction SilentlyContinue
Remove-Item $menu -Force -ErrorAction SilentlyContinue
Remove-Item $uninst -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }

if (-not $Quiet) {
    Add-Type -AssemblyName PresentationFramework
    [void][System.Windows.MessageBox]::Show("Dynamic Island was removed.", "Dynamic Island", 'OK', 'Information')
}
