# Recompile the pixel shader after editing DynamicIsland\Shaders\LiquidGlass.fx
$fxc = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter fxc.exe |
       Where-Object FullName -match '\\x64\\' | Select-Object -Last 1 -ExpandProperty FullName
$dir = "$PSScriptRoot\..\DynamicIsland\Shaders"
& $fxc /nologo /T ps_3_0 /E main /O3 /Fo "$dir\LiquidGlass.ps" "$dir\LiquidGlass.fx"
