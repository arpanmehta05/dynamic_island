# Puts the icon from an .ico file into an .exe (used for the setup program, which IExpress gives a generic icon).
#   .\set-exe-icon.ps1 -Exe path\to\file.exe -Icon path\to\file.ico
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Icon)
$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ResApi
{
    public delegate bool EnumNames(IntPtr module, IntPtr type, IntPtr name, IntPtr param);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] public static extern IntPtr LoadLibraryEx(string file, IntPtr h, uint flags);
    [DllImport("kernel32", SetLastError = true)] public static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumNames callback, IntPtr param);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] public static extern IntPtr BeginUpdateResource(string file, bool deleteExisting);
    [DllImport("kernel32", SetLastError = true)] public static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);
    [DllImport("kernel32", SetLastError = true)] public static extern bool EndUpdateResource(IntPtr update, bool discard);
}
'@

$RT_ICON = [IntPtr]3; $RT_GROUP_ICON = [IntPtr]14

# the name the exe already uses for its icon group (so the shell keeps picking it up); default 1
$module = [ResApi]::LoadLibraryEx($Exe, [IntPtr]::Zero, 0x2)   # LOAD_LIBRARY_AS_DATAFILE
$existing = [IntPtr]1
if ($module -ne [IntPtr]::Zero) {
    $names = New-Object System.Collections.Generic.List[IntPtr]
    [void][ResApi]::EnumResourceNames($module, $RT_GROUP_ICON, { param($m, $t, $n, $p) $names.Add($n); $true }, [IntPtr]::Zero)
    [void][ResApi]::FreeLibrary($module)
    if ($names.Count -gt 0) { $existing = $names[0] }
}

$ico = [IO.File]::ReadAllBytes($Icon)
$count = [BitConverter]::ToUInt16($ico, 4)
$group = New-Object System.Collections.Generic.List[byte]
$group.AddRange([byte[]](0, 0, 1, 0)); $group.AddRange([BitConverter]::GetBytes([uint16]$count))

$update = [ResApi]::BeginUpdateResource($Exe, $false)
if ($update -eq [IntPtr]::Zero) { throw "Cannot open $Exe for resource update (is it running?)" }
try {
    for ($i = 0; $i -lt $count; $i++) {
        $e = 6 + 16 * $i
        $size = [BitConverter]::ToUInt32($ico, $e + 8); $offset = [BitConverter]::ToUInt32($ico, $e + 12)
        $image = New-Object byte[] $size; [Array]::Copy($ico, $offset, $image, 0, $size)
        $id = 100 + $i
        if (-not [ResApi]::UpdateResource($update, $RT_ICON, [IntPtr]$id, 1033, $image, $size)) { throw "UpdateResource (image $i) failed" }
        $group.AddRange([byte[]]$ico[$e..($e + 11)]); $group.AddRange([BitConverter]::GetBytes([uint16]$id))
    }
    $bytes = $group.ToArray()
    if (-not [ResApi]::UpdateResource($update, $RT_GROUP_ICON, $existing, 1033, $bytes, $bytes.Length)) { throw 'UpdateResource (icon group) failed' }
}
catch { [void][ResApi]::EndUpdateResource($update, $true); throw }
if (-not [ResApi]::EndUpdateResource($update, $false)) { throw 'Could not write the icon into the exe' }
