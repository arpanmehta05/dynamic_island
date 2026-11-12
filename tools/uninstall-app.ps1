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

# undo what the optional helper scripts changed outside the program folder (Claude Code hooks, VLC's web interface)
$claudeSettings = Join-Path $env:USERPROFILE '.claude\settings.json'
$claudeScript = Join-Path $dir 'enable-claude-approvals.ps1'
if ((Test-Path $claudeScript) -and (Test-Path $claudeSettings) -and (Select-String -Path $claudeSettings -Pattern '--claude-approve' -SimpleMatch -Quiet)) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $claudeScript -Disable | Out-Null
}
$vlcScript = Join-Path $dir 'enable-vlc-controls.ps1'
if ((Test-Path $vlcScript) -and (Test-Path (Join-Path $env:LOCALAPPDATA 'DynamicIsland\vlc-web.json')) -and -not (Get-Process vlc -ErrorAction SilentlyContinue)) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $vlcScript -Disable | Out-Null
}
Remove-ItemProperty -Path $run -Name 'DynamicIsland' -ErrorAction SilentlyContinue
Remove-Item $menu -Force -ErrorAction SilentlyContinue
Remove-Item $uninst -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
# the island's own data: settings, error log, screenshot copies
foreach ($data in (Join-Path $env:APPDATA 'DynamicIsland'), (Join-Path $env:LOCALAPPDATA 'DynamicIsland')) { Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue }

if (-not $Quiet) {
    Add-Type -AssemblyName PresentationFramework
    [void][System.Windows.MessageBox]::Show("Dynamic Island was removed.", "Dynamic Island", 'OK', 'Information')
}
