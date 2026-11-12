# Installs Dynamic Island for the current user (no admin rights needed).
# Used by the Setup.exe installer and by the development script ..\install.ps1.
#   -Source  folder that contains DynamicIsland.exe (default: the folder this script is in)
#   -Quiet   no message boxes
param([string]$Source = $PSScriptRoot, [switch]$Quiet)

$ErrorActionPreference = 'Stop'
$name    = 'Dynamic Island'
$version = '1.2.0'
$dir     = Join-Path $env:LOCALAPPDATA 'Programs\DynamicIsland'
$exe     = Join-Path $dir 'DynamicIsland.exe'
$run     = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninst  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DynamicIsland'
$menu    = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Dynamic Island.lnk'
$log     = Join-Path $env:TEMP 'DynamicIslandSetup.log'

function Say($text, $icon = 'Information') {
    "$(Get-Date -Format s)  $text" | Out-File $log -Append -Encoding utf8
    if (-not $Quiet) { Add-Type -AssemblyName PresentationFramework; [void][System.Windows.MessageBox]::Show($text, "$name Setup", 'OK', $icon) }
}

try {
    "--- install $(Get-Date -Format s) from $Source" | Out-File $log -Append -Encoding utf8
    if (-not (Test-Path (Join-Path $Source 'DynamicIsland.exe'))) { throw "DynamicIsland.exe was not found in $Source" }

    # the app is built for the .NET 8 desktop runtime: check it is there instead of failing silently later
    $runtime = Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App" -Directory -ErrorAction SilentlyContinue | Where-Object Name -like '8.*'
    if (-not $runtime) {
        Say "The .NET 8 Desktop Runtime is required and was not found.`nInstall it from https://dotnet.microsoft.com/download/dotnet/8.0 (choose 'Desktop Runtime'), then run Setup again." 'Warning'
        exit 2
    }

    # stop a running copy so the file can be replaced
    Get-Process DynamicIsland -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700

    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Copy-Item (Join-Path $Source 'DynamicIsland.exe') $exe -Force
    foreach ($f in 'uninstall-app.ps1') { if (Test-Path (Join-Path $Source $f)) { Copy-Item (Join-Path $Source $f) $dir -Force } }

    # older development installs lived in %LOCALAPPDATA%\DynamicIsland: remove the stale program file (its data folder stays)
    $old = Join-Path $env:LOCALAPPDATA 'DynamicIsland\DynamicIsland.exe'
    if (Test-Path $old) { Remove-Item $old -Force -ErrorAction SilentlyContinue }

    # start with Windows
    New-Item -Path $run -Force | Out-Null
    Set-ItemProperty -Path $run -Name 'DynamicIsland' -Value "`"$exe`""

    # Start menu entry
    New-Item -ItemType Directory -Path (Split-Path $menu) -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($menu)
    $lnk.TargetPath = $exe; $lnk.WorkingDirectory = $dir; $lnk.IconLocation = "$exe,0"; $lnk.Description = $name
    $lnk.Save()

    # Settings > Apps > Installed apps (with the uninstall button)
    New-Item -Path $uninst -Force | Out-Null
    $sizeKb = [int]((Get-ChildItem $dir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
    $uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$dir\uninstall-app.ps1`""
    $values = @{
        DisplayName = $name; DisplayVersion = $version; Publisher = 'Personal'; InstallLocation = $dir; DisplayIcon = $exe
        UninstallString = $uninstallCmd; QuietUninstallString = "$uninstallCmd -Quiet"; InstallDate = (Get-Date -Format yyyyMMdd)
    }
    foreach ($k in $values.Keys) { Set-ItemProperty -Path $uninst -Name $k -Value $values[$k] }
    foreach ($k in 'NoModify', 'NoRepair') { Set-ItemProperty -Path $uninst -Name $k -Value 1 -Type DWord }
    Set-ItemProperty -Path $uninst -Name 'EstimatedSize' -Value $sizeKb -Type DWord

    Start-Process $exe
    Say "$name $version is installed and running.`n`nFind it in Task Manager > Processes > Background processes ('$name'), in the Details tab as DynamicIsland.exe, and in Settings > Apps."
}
catch {
    Say "Setup failed: $($_.Exception.Message)`nDetails: $log" 'Error'
    exit 1
}
