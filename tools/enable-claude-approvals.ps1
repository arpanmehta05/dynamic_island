# Lets Dynamic Island answer Claude Code's permission questions ("run this command?", "edit this file?") with Allow / Deny buttons,
# and its multiple-choice questions (pick one, pick several, or type your own answer).
#
#   .\enable-claude-approvals.ps1             turn it on
#   .\enable-claude-approvals.ps1 -Disable    turn it off again
#
# What it changes: adds two hooks to your Claude Code user settings (~\.claude\settings.json): "PermissionRequest" (allow/deny) and
# "PreToolUse" for the AskUserQuestion tool (questions). Both run DynamicIsland.exe --claude-approve, which hands the question to the
# running island and prints your answer back. If the island isn't running (or you tap "Ask in Claude", or don't answer for 75 s) it
# prints nothing and Claude Code asks you as usual. Your other settings are kept; a backup is saved next to the file.
# Takes effect for new Claude Code sessions.
param(
    [switch]$Disable,
    [string]$SettingsPath = (Join-Path $env:USERPROFILE '.claude\settings.json'),
    [string]$Exe = (Join-Path $env:LOCALAPPDATA 'Programs\DynamicIsland\DynamicIsland.exe')
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$marker = '--claude-approve'

$settings = if (Test-Path $SettingsPath) { [IO.File]::ReadAllText($SettingsPath, $utf8) | ConvertFrom-Json } else { [pscustomobject]@{} }
if (-not $settings.PSObject.Properties['hooks']) { $settings | Add-Member hooks ([pscustomobject]@{}) }
$hooks = $settings.hooks

if (-not $Disable -and -not (Test-Path $Exe)) { throw "Dynamic Island is not installed at $Exe. Run the installer first." }

foreach ($event in @(@{ Name = 'PermissionRequest'; Matcher = '' }, @{ Name = 'PreToolUse'; Matcher = 'AskUserQuestion' })) {
    # drop any earlier copy of our hook (keeps your other hooks on the same event)
    $kept = @()
    if ($hooks.PSObject.Properties[$event.Name]) {
        foreach ($group in @($hooks.($event.Name))) {
            $rest = @($group.hooks | Where-Object { $_.command -notlike "*$marker*" })
            if ($rest.Count -gt 0) { $group.hooks = $rest; $kept += $group }
        }
    }
    if (-not $Disable) {
        $kept += [pscustomobject]@{ matcher = $event.Matcher; hooks = @([pscustomobject]@{ type = 'command'; command = "`"$Exe`" $marker"; timeout = 90 }) }
    }
    if ($kept.Count -gt 0) {
        if ($hooks.PSObject.Properties[$event.Name]) { $hooks.($event.Name) = $kept } else { $hooks | Add-Member $event.Name $kept }
    } elseif ($hooks.PSObject.Properties[$event.Name]) {
        $hooks.PSObject.Properties.Remove($event.Name)
    }
}
if (@($hooks.PSObject.Properties).Count -eq 0) { $settings.PSObject.Properties.Remove('hooks') }

if (Test-Path $SettingsPath) { Copy-Item $SettingsPath "$SettingsPath.island-backup" -Force }
New-Item -ItemType Directory (Split-Path $SettingsPath) -Force | Out-Null
[IO.File]::WriteAllText($SettingsPath, ($settings | ConvertTo-Json -Depth 20), $utf8)

if ($Disable) { Write-Host 'Claude approvals are off again.' -ForegroundColor Green }
else { Write-Host 'Claude approvals and questions are on. New Claude Code sessions will ask through the island first.' -ForegroundColor Green
       Write-Host "Backup: $SettingsPath.island-backup   |   To undo: .\enable-claude-approvals.ps1 -Disable" }
