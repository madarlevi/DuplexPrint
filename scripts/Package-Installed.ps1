[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.2.0',
    [string]$Publisher = 'CN=madarlevi',
    [string]$OutputDirectory = '',
    [switch]$FrameworkDependent,
    [string[]]$MSBuildArguments = @(),
    [string]$CertificatePath = '',
    [string]$CertificatePasswordEnvironment = 'DUPLEXPRINT_CERTIFICATE_PASSWORD',
    [string]$TimestampUrl = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/packages' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$sdkVersion = Get-ChildItem -LiteralPath $sdk -Directory | Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and [version]$_.Name -ge [version]'10.0.26100.0' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdkVersion) { throw 'Windows SDK 10.0.26100.0 or later is required for the installed edition.' }
$makeappx = Join-Path $sdkVersion.FullName 'x64/makeappx.exe'
$signtool = Join-Path $sdkVersion.FullName 'x64/signtool.exe'

# Reuse the same validated publish pipeline, without the portable settings marker.
$published = @(& (Join-Path $PSScriptRoot 'Package.ps1') -Version $Version -NoPortableMarker -FrameworkDependent:$FrameworkDependent -MSBuildArguments $MSBuildArguments -OutputDirectory (Join-Path $repo 'artifacts/installed-publish')) | Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['Root'] } | Select-Object -Last 1
if (!$published) { throw 'The application publish did not return a staging directory.' }
$root = Join-Path $repo ('artifacts/msix-work/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null
Copy-Item -LiteralPath $published.Root -Destination (Join-Path $root 'App') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'installer/Config') -Destination $root -Recurse
& powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-BrandAssets.ps1') -OutputDirectory (Join-Path $root 'Assets')
if ($LASTEXITCODE -ne 0) { throw 'Brand asset generation failed.' }
& (Join-Path $PSScriptRoot 'Build-PdfWorker.ps1') -Component CaptureTask -OutputDirectory (Join-Path $root 'native')
Copy-Item -LiteralPath (Join-Path $root 'native/DuplexPrint.Capture.dll') -Destination $root
# Compiler outputs stay outside the MSIX payload. Move the generated app files to a fresh staging root.
$payload = Join-Path $root 'payload'
New-Item -ItemType Directory -Force -Path $payload | Out-Null
foreach ($item in @('App', 'Config', 'Assets', 'DuplexPrint.Capture.dll')) { Copy-Item -LiteralPath (Join-Path $root $item) -Destination $payload -Recurse }
[xml]$manifest = Get-Content -Raw -LiteralPath (Join-Path $repo 'installer/AppxManifest.xml')
$manifest.Package.Identity.Version = "$Version.0"
$manifest.Package.Identity.Publisher = $Publisher
$manifest.Save((Join-Path $payload 'AppxManifest.xml'))
$suffix = if ($FrameworkDependent) { '-framework-dependent' } else { '' }
$signature = if ($CertificatePath) { '' } else { '-unsigned' }
$package = Join-Path $OutputDirectory "DuplexPrint-v$Version-win-x64-installed$suffix$signature.msix"
& $makeappx pack /d $payload /p $package /o /h SHA256
if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging or manifest validation failed.' }

if ($CertificatePath) {
    $password = [Environment]::GetEnvironmentVariable($CertificatePasswordEnvironment)
    if (!$password) { throw "Set $CertificatePasswordEnvironment to the signing certificate password." }
    $signArguments = @('sign', '/fd', 'SHA256', '/f', [IO.Path]::GetFullPath($CertificatePath), '/p', $password)
    if ($TimestampUrl) { $signArguments += @('/tr', $TimestampUrl, '/td', 'SHA256') }
    & $signtool @signArguments $package
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed. Check that the certificate subject matches Publisher.' }
    & $signtool verify /pa $package
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signature verification failed.' }
}

# Inspect the actual archive, including the registered capture DLL and application layout.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $entries = @($archive.Entries.FullName | ForEach-Object { $_.Replace('\', '/') })
    foreach ($required in @('AppxManifest.xml', 'DuplexPrint.Capture.dll', 'App/DuplexPrint.exe', 'App/DuplexPrint.PdfWorker.exe', 'Config/PrinterCapabilities.xml')) {
        if ($entries -cnotcontains $required) { throw "MSIX is missing $required" }
    }
    if ($entries | Where-Object { $_ -match '(?i)(SumatraPDF|\.(pdb|obj|lib|exp|pfx|key)$|/settings\.json$|/portable\.mode$)' }) { throw 'Prohibited file in MSIX.' }
}
finally { $archive.Dispose() }
Get-FileHash -Algorithm SHA256 -LiteralPath $package | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($package))" } | Set-Content -Encoding ascii -LiteralPath "$package.sha256"
Write-Host "Validated MSIX: $package"
if (!$CertificatePath) { Write-Warning 'UNSIGNED DEVELOPMENT PACKAGE: sign with a trusted matching publisher certificate before end-user installation.' }
