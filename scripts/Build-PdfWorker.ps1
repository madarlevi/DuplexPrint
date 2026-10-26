[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateSet('PdfWorker', 'CaptureTask', 'CaptureTests')][string]$Component = 'PdfWorker')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ Build Tools and the Windows 10/11 SDK to build the PDF worker.' }
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or !$vs) { throw 'Visual Studio C++ x64 tools were not found.' }
$toolset = Get-ChildItem -LiteralPath (Join-Path $vs 'VC/Tools/MSVC') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$sdk = if ($env:WindowsSdkDir) { $env:WindowsSdkDir } else { Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10' }
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdk 'Include') -Directory |
    Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and [version]$_.Name -ge [version]'10.0.19041.0' } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$toolset -or !$sdkVersion) { throw 'A current MSVC toolset and Windows SDK 10.0.19041.0 or later are required.' }
$compiler = Join-Path $toolset.FullName 'bin/Hostx64/x64/cl.exe'
$source = Join-Path $repo $(if ($Component -eq 'PdfWorker') { 'native/PdfWorker/main.cpp' } elseif ($Component -eq 'CaptureTask') { 'native/CaptureTask/main.cpp' } else { 'native/CaptureTask/tests.cpp' })
if ($Component -ne 'PdfWorker' -and [version]$sdkVersion.Name -lt [version]'10.0.26100.0') { throw 'The installed edition requires Windows SDK 10.0.26100.0 or later.' }
$binary = if ($Component -eq 'PdfWorker') { 'DuplexPrint.PdfWorker.exe' } elseif ($Component -eq 'CaptureTask') { 'DuplexPrint.Capture.dll' } else { 'DuplexPrint.CaptureTests.exe' }
# Direct invocation, no generated shell command: spaces, Unicode, &, and % are literal paths.
$compileArguments = @('/nologo', '/std:c++20', '/EHsc', '/MT', '/O2', '/W4', '/WX', '/utf-8',
    '/permissive-', '/guard:cf', '/sdl', '/DUNICODE', '/D_UNICODE', '/DNOMINMAX', '/DWIN32_LEAN_AND_MEAN', '/D_WIN32_WINNT=0x0A00',
    '/I', (Join-Path $toolset.FullName 'include'))
foreach ($include in @('ucrt', 'shared', 'um', 'winrt', 'cppwinrt')) {
    $compileArguments += @('/I', (Join-Path $sdkVersion.FullName $include))
}
if ($Component -eq 'CaptureTask') { $compileArguments += '/LD' }
$compileArguments += @(("/Fo" + (Join-Path $OutputDirectory "$Component.obj")),
    ("/Fe" + (Join-Path $OutputDirectory $binary)), $source, '/link', 'windowsapp.lib',
    ("/LIBPATH:" + (Join-Path $toolset.FullName 'lib/x64')),
    ("/LIBPATH:" + (Join-Path $sdk "Lib/$($sdkVersion.Name)/um/x64")),
    ("/LIBPATH:" + (Join-Path $sdk "Lib/$($sdkVersion.Name)/ucrt/x64")),
    '/DYNAMICBASE', '/NXCOMPAT', '/CETCOMPAT', '/OPT:REF', '/OPT:ICF', '/INCREMENTAL:NO')
$previousPath = $env:PATH
if ($Component -eq 'CaptureTask') { $compileArguments += '/EXPORT:DllCanUnloadNow,PRIVATE' }
try {
    # cl uses the matching linker from this toolset, not an unrelated link.exe on PATH.
    $env:PATH = (Split-Path -Parent $compiler) + [IO.Path]::PathSeparator + $previousPath
    & $compiler @compileArguments
    if ($LASTEXITCODE -ne 0) { throw 'Native PDF worker compilation failed.' }
}
finally { $env:PATH = $previousPath }
