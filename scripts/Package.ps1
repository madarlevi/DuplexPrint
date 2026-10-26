[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$')]
    [string]$Version = '1.2.0',
    [string]$OutputDirectory = '',
    [switch]$FrameworkDependent,
    [switch]$NoPortableMarker,
    [string[]]$MSBuildArguments = @()
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/packages' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$work = Join-Path $repo ('artifacts/package-work/' + [Guid]::NewGuid().ToString('N'))
$name = "DuplexPrint-v$Version-win-x64"
if ($FrameworkDependent) { $name += '-framework-dependent' }
$packageRoot = Join-Path $work $name
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
$project = Join-Path $repo 'DuplexPrint/DuplexPrint.csproj'
$contained = if ($FrameworkDependent) { 'false' } else { 'true' }

# A fresh publish directory cannot inherit obsolete dependencies or old builds.
& dotnet publish $project -c Release -r win-x64 --self-contained $contained -o $packageRoot `
    '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' `
    '-p:DebugType=None' '-p:DebugSymbols=false' "-p:Version=$Version" '-p:RestoreLockedMode=true' @MSBuildArguments
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed; no ZIP was created.' }

Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $packageRoot 'THIRD_PARTY_NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination (Join-Path $packageRoot 'README.md')
$readmePath = Join-Path $packageRoot 'README.md'
$readme = Get-Content -Raw -LiteralPath $readmePath
$readme.Replace('(LICENSE)', '(LICENSE.txt)').Replace('(THIRD_PARTY_NOTICES.md)', '(THIRD_PARTY_NOTICES.txt)').Replace(
    '(.github/workflows/build.yml)', '(https://github.com/madarlevi/DuplexPrint/blob/main/.github/workflows/build.yml)').Replace(
    '(.github/workflows/release.yml)', '(https://github.com/madarlevi/DuplexPrint/blob/main/.github/workflows/release.yml)') |
    Set-Content -Encoding utf8 -LiteralPath $readmePath
Copy-Item -LiteralPath (Join-Path $repo 'licenses') -Destination $packageRoot -Recurse
# README.md's screenshots remain usable when the ZIP is read offline.
Copy-Item -LiteralPath (Join-Path $repo 'assets') -Destination $packageRoot -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination $packageRoot -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'configuration') -Destination $packageRoot -Recurse
if (!$NoPortableMarker) { Set-Content -Encoding ascii -LiteralPath (Join-Path $packageRoot 'portable.mode') -Value 'Keep settings.json beside DuplexPrint.exe.' }

$allowedExecutables = @('DuplexPrint.exe', 'DuplexPrint.PdfWorker.exe')
if (!$FrameworkDependent) {
    $assets = Get-Content -Raw -LiteralPath (Join-Path $repo 'DuplexPrint/obj/project.assets.json') | ConvertFrom-Json
    $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
    foreach ($pack in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $downloads = @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Where-Object { $_.name -ieq $pack })
        if ($downloads.Count -ne 1) { throw "Cannot determine the restored runtime pack version: $pack" }
        $packVersion = $downloads[0].version.Trim('[', ']').Split(',')[0].Trim()
        $packPath = $null
        foreach ($folder in $packageFolders) {
            $candidate = Join-Path $folder "$pack/$packVersion"
            if (Test-Path -LiteralPath $candidate) { $packPath = $candidate; break }
        }
        if (!$packPath) { throw "Cannot find restored runtime pack: $pack/$packVersion" }
        $destination = Join-Path $packageRoot "licenses/runtime/$pack-$packVersion"
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        $notices = @(Get-ChildItem -LiteralPath $packPath -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|THIRD[-_]?PARTY[-_]?NOTICES)(\.(txt|md))?$' })
        if (!($notices | Where-Object { $_.Name -match '^LICENSE(\.|$)' }) -or
            !($notices | Where-Object { $_.Name -match '^THIRD[-_]?PARTY[-_]?NOTICES(\.|$)' })) {
            throw "Runtime license/notice missing from $packPath. Review before distribution."
        }
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $destination }
        # The .NET runtime includes its own crash-dump helper. Accept it only when
        # it exactly matches the restored Microsoft runtime pack; never accept
        # an arbitrary additional executable from the application directory.
        if ($pack -eq 'microsoft.netcore.app.runtime.win-x64' -and (Test-Path -LiteralPath (Join-Path $packageRoot 'createdump.exe'))) {
            $dumpSources = @(Get-ChildItem -LiteralPath $packPath -Recurse -File -Filter createdump.exe)
            if ($dumpSources.Count -ne 1 -or (Get-FileHash -LiteralPath $dumpSources[0].FullName).Hash -ne
                (Get-FileHash -LiteralPath (Join-Path $packageRoot 'createdump.exe')).Hash) { throw 'Runtime createdump.exe does not match the restored Microsoft runtime pack.' }
            $allowedExecutables += 'createdump.exe'
        }
    }
}

$files = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File)
if ($files | Where-Object { ($_.Extension -eq '.exe' -and $_.Name -notin $allowedExecutables) -or
    $_.Extension -in @('.pdb', '.tmp', '.zip', '.obj', '.lib', '.ilk', '.pfx', '.key') -or $_.Name -eq 'settings.json' -or $_.Name -match 'SumatraPDF' }) {
    throw 'Unexpected executable, build intermediate or archive in publish output.'
}
foreach ($required in @('DuplexPrint.exe', 'DuplexPrint.PdfWorker.exe', 'DuplexPrint.dll', 'DuplexPrint.runtimeconfig.json', 'PdfSharp.dll', 'LICENSE.txt', 'THIRD_PARTY_NOTICES.txt', 'licenses/CppWinRT-LICENSE.txt', 'licenses/Microsoft-STL-LICENSE.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $packageRoot $required))) { throw "Missing package file: $required" }
}
if (!$FrameworkDependent -and !(Test-Path -LiteralPath (Join-Path $packageRoot 'coreclr.dll'))) {
    throw 'Self-contained runtime is missing.'
}

$zip = Join-Path $OutputDirectory "$name.zip"
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zip -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($archive.Entries.FullName | ForEach-Object { $_.Replace('\', '/') })
    foreach ($required in @('DuplexPrint.exe', 'DuplexPrint.PdfWorker.exe', 'LICENSE.txt', 'THIRD_PARTY_NOTICES.txt')) {
        if ($entries -cnotcontains "$name/$required") { throw "ZIP is missing $required" }
    }
    if ($entries | Where-Object { $_ -match '(?i)(\.(pdb|tmp|obj|lib|ilk)$|/(bin|obj|\.vs)/)' -or
        ($_ -like '*.exe' -and [IO.Path]::GetFileName($_) -notin $allowedExecutables) }) {
        throw 'ZIP validation found prohibited files.'
    }
}
finally { $archive.Dispose() }
Get-FileHash -Algorithm SHA256 -LiteralPath $zip | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($zip))" } |
    Set-Content -Encoding ascii -LiteralPath "$zip.sha256"
Write-Host "Validated package: $zip"
Write-Host "Staging directory: $packageRoot"
[pscustomobject]@{ Root = $packageRoot; Zip = $zip; Name = $name }
