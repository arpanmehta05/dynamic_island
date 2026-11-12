# Switches on VLC's built-in web interface (local only) so Dynamic Island can show VLC's exact play/pause state, timeline and
# +/-10 second buttons, and seek. VLC has no other way of telling another program these things.
#
#   .\enable-vlc-controls.ps1             turn it on   (close VLC first: VLC rewrites its settings when it quits)
#   .\enable-vlc-controls.ps1 -Disable    turn it off again and restore the defaults
#
# What it changes: four lines in VLC's settings file (%APPDATA%\vlc\vlcrc) - the web interface on, bound to 127.0.0.1 only (not visible
# to the network), on a free port, with a random password. A backup of the settings file is kept next to it. The port and password
# are saved for the island in %LOCALAPPDATA%\DynamicIsland\vlc-web.json. Nothing is sent anywhere.
param(
    [switch]$Disable,
    [string]$ConfigPath   = (Join-Path $env:APPDATA 'vlc\vlcrc'),
    [string]$SettingsPath = (Join-Path $env:LOCALAPPDATA 'DynamicIsland\vlc-web.json'),
    [int]$Port = 0,
    [switch]$SkipRunningCheck   # for testing against a copy of the settings
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
# VLC writes its settings with a byte-order mark; keep whatever the file had
$bom = (Test-Path $ConfigPath) -and ([IO.File]::ReadAllBytes($ConfigPath) | Select-Object -First 3) -join ',' -eq '239,187,191'
$utf8File = New-Object System.Text.UTF8Encoding($bom)

if (-not $SkipRunningCheck -and (Get-Process vlc -ErrorAction SilentlyContinue)) {
    Write-Host 'VLC is running. Close it first (VLC overwrites its settings file when it quits), then run this again.' -ForegroundColor Yellow
    exit 2
}
if (-not (Test-Path $ConfigPath)) { Write-Host "VLC's settings file was not found at $ConfigPath. Start VLC once and close it, then run this again." -ForegroundColor Yellow; exit 3 }

# keep lines with their original line endings
$lines = [System.Collections.Generic.List[string]]([regex]::Split([IO.File]::ReadAllText($ConfigPath, $utf8), '(?<=\n)'))
if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }

function Set-Option([string]$key, [string]$value, [bool]$enable) {
    $pattern = '^\s*#?\s*' + [regex]::Escape($key) + '\s*='
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $pattern) {
            $eol = if ($lines[$i].EndsWith("`r`n")) { "`r`n" } else { "`n" }
            $lines[$i] = $(if ($enable) { "$key=$value" } else { "#$key=$($script:defaults[$key])" }) + $eol
            return
        }
    }
    throw "The option '$key' was not found in VLC's settings file; this VLC version is different from what the script expects. Nothing was changed."
}

$defaults = @{ 'extraintf' = ''; 'http-host' = ''; 'http-port' = '8080'; 'http-password' = '' }

if ($Disable) {
    foreach ($k in 'extraintf', 'http-host', 'http-port', 'http-password') { Set-Option $k '' $false }
    [IO.File]::WriteAllText($ConfigPath, ($lines -join ''), $utf8File)
    Remove-Item $SettingsPath -Force -ErrorAction SilentlyContinue
    Write-Host 'VLC web controls are off again.' -ForegroundColor Green
    exit 0
}

# a port nothing is using (VLC's default 8080 is exactly what dev servers like to take)
if ($Port -eq 0) {
    $busy = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | ForEach-Object { $_.LocalPort })
    $Port = (9780..9799 | Where-Object { $busy -notcontains $_ } | Select-Object -First 1)
    if (-not $Port) { throw 'No free port found between 9780 and 9799.' }
}
$chars = [char[]]'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
$password = -join (1..24 | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })

Copy-Item $ConfigPath "$ConfigPath.island-backup" -Force
Set-Option 'extraintf'     'http'      $true
Set-Option 'http-host'     '127.0.0.1' $true
Set-Option 'http-port'     "$Port"     $true
Set-Option 'http-password' $password   $true
[IO.File]::WriteAllText($ConfigPath, ($lines -join ''), $utf8File)

New-Item -ItemType Directory -Path (Split-Path $SettingsPath) -Force | Out-Null
[IO.File]::WriteAllText($SettingsPath, (@{ port = $Port; password = $password } | ConvertTo-Json), $utf8)

Write-Host "VLC web controls are on (127.0.0.1:$Port, local only). Start VLC again; Dynamic Island will pick it up within a few seconds." -ForegroundColor Green
Write-Host "Backup of your settings: $ConfigPath.island-backup   |   To undo: .\enable-vlc-controls.ps1 -Disable"
