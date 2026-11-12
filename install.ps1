# Development install: build the app and install it exactly like the Setup.exe does.
#   .\install.ps1            build + install + start
#   .\install.ps1 -Remove    uninstall
param([switch]$Remove)

if ($Remove) { & "$PSScriptRoot\tools\uninstall-app.ps1" -Quiet; return }

$payload = "$PSScriptRoot\installer\payload"
Get-Process DynamicIsland -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
dotnet publish "$PSScriptRoot\DynamicIsland" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o $payload
if ($LASTEXITCODE) { throw 'publish failed' }
Copy-Item "$PSScriptRoot\tools\uninstall-app.ps1" $payload

& "$PSScriptRoot\tools\install-app.ps1" -Source $payload -Quiet
