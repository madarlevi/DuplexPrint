# Shared by packaging and the offline packaging regression tests.
function Copy-RuntimeNotices {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$CorePackPath,
        [Parameter(Mandatory = $true)][string]$DesktopPackPath,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory,
        [Parameter(Mandatory = $true)][string]$DesktopMitLicensePath
    )
    $ErrorActionPreference = 'Stop'
    $packPaths = [ordered]@{
        'microsoft.netcore.app.runtime.win-x64' = $CorePackPath
        'microsoft.windowsdesktop.app.runtime.win-x64' = $DesktopPackPath
    }
    $sources = [Collections.Generic.List[string]]::new()
    $coreNoticeLocation = $null
    foreach ($entry in $packPaths.GetEnumerator()) {
        $pack = $entry.Key
        $packPath = [IO.Path]::GetFullPath($entry.Value).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        if (!(Test-Path -LiteralPath $packPath -PathType Container)) { throw "Runtime pack not found: $packPath" }
        $version = Split-Path -Leaf $packPath
        $folder = "$pack-$version"
        $destination = Join-Path $DestinationDirectory $folder
        $notices = @(Get-ChildItem -LiteralPath $packPath -Recurse -File | Where-Object {
            $_.Name -match '^(LICENSE(?:[-_.][A-Za-z0-9]+)*|THIRD[-_]?PARTY[-_]?NOTICES)(\.(txt|md))?$'
        })
        $licenses = @($notices | Where-Object { $_.Name -match '^LICENSE([-_.]|$)' })
        $thirdParty = @($notices | Where-Object { $_.Name -match '^THIRD[-_]?PARTY[-_]?NOTICES(\.|$)' })
        if ($pack -eq 'microsoft.netcore.app.runtime.win-x64' -and (!$licenses.Count -or !$thirdParty.Count)) {
            throw "Core runtime license/combined third-party notices missing from $packPath. Restore the runtime pack again before distribution."
        }
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        foreach ($notice in $notices) {
            if ($notice.Length -eq 0) { throw "Empty runtime license/notice: $($notice.FullName)" }
            # Preserve paths and bytes; different component notices must not overwrite each other.
            $relative = $notice.FullName.Substring($packPath.Length + 1)
            $target = Join-Path $destination $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            Copy-Item -LiteralPath $notice.FullName -Destination $target
            $sources.Add("$folder/$($relative.Replace('\', '/')): unchanged from the restored NuGet runtime pack.")
        }
        if ($pack -eq 'microsoft.netcore.app.runtime.win-x64') {
            $relative = $thirdParty[0].FullName.Substring($packPath.Length + 1).Replace('\', '/')
            $coreNoticeLocation = "$folder/$relative"
        }
        elseif (!$licenses.Count) {
            # Some Desktop packs use an SPDX expression instead of a physical license file.
            # Use the reviewed upstream MIT text only if this exact package declares MIT.
            $nuspec = Join-Path $packPath ($pack + '.nuspec')
            if (!(Test-Path -LiteralPath $nuspec -PathType Leaf)) { throw "Desktop runtime license metadata missing: $nuspec" }
            [xml]$metadata = Get-Content -Raw -LiteralPath $nuspec
            $license = $metadata.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='license']")
            if ($null -eq $license -or $license.GetAttribute('type') -ne 'expression' -or $license.InnerText.Trim() -ne 'MIT') {
                throw 'The Desktop runtime has no license file and does not declare the reviewed MIT license expression. Review the new package license before distribution.'
            }
            if (!(Test-Path -LiteralPath $DesktopMitLicensePath -PathType Leaf)) { throw "Reviewed Desktop MIT text missing: $DesktopMitLicensePath" }
            Copy-Item -LiteralPath $DesktopMitLicensePath -Destination (Join-Path $destination 'LICENSE.txt')
            $sources.Add("$folder/LICENSE.txt: reviewed dotnet/windowsdesktop MIT text; confirmed against this runtime pack's SPDX MIT metadata.")
        }
        if ($pack -eq 'microsoft.windowsdesktop.app.runtime.win-x64' -and !$thirdParty.Count) {
            if (!$coreNoticeLocation) { throw 'The combined .NET third-party notices were not copied.' }
            $sources.Add("${folder}: no separate third-party-notice file is supplied by this pack. The combined .NET distribution notices are in $coreNoticeLocation.")
        }
    }
    $sources.Add('Microsoft explains the combined distribution notices: https://github.com/dotnet/runtime/blob/main/src/installer/pkg/README.md')
    $sources.Add('Windows .NET binary/component license terms: https://github.com/dotnet/core/blob/main/license-information-windows.md')
    [IO.File]::WriteAllLines((Join-Path $DestinationDirectory 'NOTICE-SOURCES.txt'), $sources, [Text.UTF8Encoding]::new($false))
}
