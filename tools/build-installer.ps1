# Builds installer\DynamicIslandSetup.exe: publishes the app and wraps it, with the install/uninstall scripts, in a self-extracting
# setup program made by IExpress (which is part of Windows, so nothing extra needs installing).
#   .\tools\build-installer.ps1
$ErrorActionPreference = 'Stop'
$root    = Split-Path $PSScriptRoot -Parent
$payload = Join-Path $root 'installer\payload'
$final   = Join-Path $root 'installer\DynamicIslandSetup.exe'

$work = Join-Path $env:TEMP 'dynamic-island-build'   # IExpress cannot handle spaces in paths, so it packages from here
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
$stage  = Join-Path $work 'payload'
$target = Join-Path $work 'DynamicIslandSetup.exe'
$sed    = Join-Path $work 'setup.sed'
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory $payload -Force | Out-Null
New-Item -ItemType Directory $stage -Force | Out-Null

dotnet publish (Join-Path $root 'DynamicIsland') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o $payload
if ($LASTEXITCODE) { throw 'publish failed' }

Copy-Item (Join-Path $PSScriptRoot 'install-app.ps1')   $payload
Copy-Item (Join-Path $PSScriptRoot 'uninstall-app.ps1') $payload
[IO.File]::WriteAllText((Join-Path $payload 'install.cmd'), "@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0install-app.ps1`" %*
", (New-Object Text.ASCIIEncoding))

$files = 'DynamicIsland.exe', 'install-app.ps1', 'uninstall-app.ps1', 'install.cmd'
foreach ($f in $files) { Copy-Item (Join-Path $payload $f) $stage }
$fileStrings = ($files | ForEach-Object -Begin { $i = 0 } -Process { "FILE$i=`"$_`""; $i++ }) -join "`r`n"
$fileRefs    = ($files | ForEach-Object -Begin { $i = 0 } -Process { "%FILE$i%="; $i++ }) -join "`r`n"

@"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=Install Dynamic Island for this user?
DisplayLicense=
FinishMessage=
TargetName=$target
FriendlyName=Dynamic Island Setup
AppLaunched=cmd /c .\install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=cmd /c .\install.cmd -Quiet
$fileStrings
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
$fileRefs
"@ | Set-Content $sed -Encoding ascii

if (Test-Path $target) { Remove-Item $target -Force }
& "$env:SystemRoot\System32\iexpress.exe" /N /Q $sed | Out-Null
# IExpress returns immediately; wait for the file to appear
for ($i = 0; $i -lt 60 -and -not (Test-Path $target); $i++) { Start-Sleep -Milliseconds 500 }
if (-not (Test-Path $target)) { throw 'IExpress did not produce the installer (see the .sed file in ' + $work + ')' }
& (Join-Path $PSScriptRoot 'set-exe-icon.ps1') -Exe $target -Icon (Join-Path $root 'DynamicIsland\App.ico')
Copy-Item $target $final -Force
"Built $final  ($([Math]::Round((Get-Item $target).Length / 1MB, 1)) MB)"
