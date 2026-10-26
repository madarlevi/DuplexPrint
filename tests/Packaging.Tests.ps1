# Offline regression tests. These fixtures are intentionally synthetic text,
# not Microsoft runtime packs; no application build, printer or network is used.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $repo 'scripts/RuntimeNotices.ps1')
$base = Join-Path $repo 'artifacts/packaging-tests'
New-Item -ItemType Directory -Force -Path $base | Out-Null
$testRoot = Join-Path $base ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$script:passed = 0
$script:failed = 0

function Check([string]$Name, [scriptblock]$Test) {
    try { & $Test; $script:passed++; Write-Host "PASS $Name" }
    catch { $script:failed++; Write-Host "FAIL $Name : $($_.Exception.Message)" -ForegroundColor Red }
}
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function ExpectFailure([scriptblock]$Action, [string]$Pattern) {
    $caught = $null
    try { & $Action } catch { $caught = $_.Exception.Message }
    Assert ($null -ne $caught -and $caught -like $Pattern) "Expected failure '$Pattern'; got '$caught'."
}
function WriteFixture([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($true))
}
function NewFixture {
    $path = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
    $core = Join-Path $path 'microsoft.netcore.app.runtime.win-x64/10.0.14'
    $desktop = Join-Path $path 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.14'
    New-Item -ItemType Directory -Force -Path $core, $desktop | Out-Null
    WriteFixture (Join-Path $core 'LICENSE.txt') '[TEST FIXTURE ONLY] Core license — Unicode ő.'
    WriteFixture (Join-Path $core 'THIRD-PARTY-NOTICES.TXT') '[TEST FIXTURE ONLY] Combined .NET distribution notices.'
    WriteFixture (Join-Path $desktop 'LICENSE') '[TEST FIXTURE ONLY] Desktop license.'
    return @{ CorePackPath = $core; DesktopPackPath = $desktop; DestinationDirectory = (Join-Path $path 'published/licenses/runtime'); DesktopMitLicensePath = (Join-Path $repo 'licenses/Microsoft.WindowsDesktop.App-MIT-LICENSE.txt') }
}
function WriteMetadata([hashtable]$Fixture, [string]$LicenseType, [string]$LicenseValue) {
    WriteFixture (Join-Path $Fixture.DesktopPackPath 'microsoft.windowsdesktop.app.runtime.win-x64.nuspec') "<package xmlns='http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'><metadata><license type='$LicenseType'>$LicenseValue</license></metadata></package>"
}

try {
    Check 'Desktop license-only pack uses the combined Core notices and records their source' {
        $fixture = NewFixture
        Copy-RuntimeNotices @fixture
        $output = $fixture.DestinationDirectory
        Assert (Test-Path -LiteralPath (Join-Path $output 'microsoft.windowsdesktop.app.runtime.win-x64-10.0.14/LICENSE')) 'Desktop license not copied.'
        $index = [IO.File]::ReadAllText((Join-Path $output 'NOTICE-SOURCES.txt'))
        Assert ($index.Contains('combined .NET distribution notices') -and $index.Contains('microsoft.netcore.app.runtime.win-x64-10.0.14/THIRD-PARTY-NOTICES.TXT')) 'No pointer to the included combined notice.'
        Assert (!$index.Contains($testRoot)) 'Source index leaked a machine/cache path.'
    }
    Check 'Actual embedded notice bytes and separate nested component licenses are retained' {
        $fixture = NewFixture
        WriteFixture (Join-Path $fixture.CorePackPath 'components/example/LICENSE.txt') '[TEST FIXTURE ONLY] A separate component.'
        Copy-RuntimeNotices @fixture
        foreach ($relative in @('LICENSE.txt', 'THIRD-PARTY-NOTICES.TXT', 'components/example/LICENSE.txt')) {
            $source = Join-Path $fixture.CorePackPath $relative
            $target = Join-Path $fixture.DestinationDirectory ('microsoft.netcore.app.runtime.win-x64-10.0.14/' + $relative)
            Assert ((Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $target).Hash) "Changed/clobbered bytes: $relative"
        }
    }
    Check 'Desktop SPDX-only MIT metadata receives the reviewed upstream license text' {
        $fixture = NewFixture
        Remove-Item -LiteralPath (Join-Path $fixture.DesktopPackPath 'LICENSE')
        WriteMetadata $fixture 'expression' 'MIT'
        Copy-RuntimeNotices @fixture
        $target = Join-Path $fixture.DestinationDirectory 'microsoft.windowsdesktop.app.runtime.win-x64-10.0.14/LICENSE.txt'
        Assert ((Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $fixture.DesktopMitLicensePath).Hash) 'Reviewed fallback license changed.'
    }
    Check 'A supplied Desktop third-party notice is copied and not replaced' {
        $fixture = NewFixture
        WriteFixture (Join-Path $fixture.DesktopPackPath 'ThirdPartyNotices.txt') '[TEST FIXTURE ONLY] Desktop-specific notices.'
        Copy-RuntimeNotices @fixture
        Assert (Test-Path -LiteralPath (Join-Path $fixture.DestinationDirectory 'microsoft.windowsdesktop.app.runtime.win-x64-10.0.14/ThirdPartyNotices.txt')) 'Desktop notice lost.'
        $index = [IO.File]::ReadAllText((Join-Path $fixture.DestinationDirectory 'NOTICE-SOURCES.txt'))
        Assert (!$index.Contains('no separate third-party-notice')) 'Incorrect fallback recorded.'
    }
    Check 'Missing combined Core third-party notices still fail packaging' {
        $fixture = NewFixture
        Remove-Item -LiteralPath (Join-Path $fixture.CorePackPath 'THIRD-PARTY-NOTICES.TXT')
        ExpectFailure { Copy-RuntimeNotices @fixture } '*Core runtime license/combined third-party notices missing*'
    }
    Check 'Missing Core license still fails packaging' {
        $fixture = NewFixture
        Remove-Item -LiteralPath (Join-Path $fixture.CorePackPath 'LICENSE.txt')
        ExpectFailure { Copy-RuntimeNotices @fixture } '*Core runtime license/combined third-party notices missing*'
    }
    Check 'A changed Desktop license expression cannot silently become MIT' {
        $fixture = NewFixture
        Remove-Item -LiteralPath (Join-Path $fixture.DesktopPackPath 'LICENSE')
        WriteMetadata $fixture 'expression' 'Apache-2.0'
        ExpectFailure { Copy-RuntimeNotices @fixture } '*does not declare the reviewed MIT license expression*'
    }
    Check 'Missing Desktop license metadata still fails packaging' {
        $fixture = NewFixture
        Remove-Item -LiteralPath (Join-Path $fixture.DesktopPackPath 'LICENSE')
        ExpectFailure { Copy-RuntimeNotices @fixture } '*Desktop runtime license metadata missing*'
    }
    Check 'A missing metadata-referenced license file is not treated as SPDX MIT' {
        $fixture = NewFixture
        Remove-Item -LiteralPath (Join-Path $fixture.DesktopPackPath 'LICENSE')
        WriteMetadata $fixture 'file' 'LICENSE.txt'
        ExpectFailure { Copy-RuntimeNotices @fixture } '*does not declare the reviewed MIT license expression*'
    }
    Check 'Empty legal documents are rejected' {
        $fixture = NewFixture
        [IO.File]::WriteAllBytes((Join-Path $fixture.CorePackPath 'THIRD-PARTY-NOTICES.TXT'), [byte[]]@())
        ExpectFailure { Copy-RuntimeNotices @fixture } '*Empty runtime license/notice*'
    }
    Check 'Missing reviewed MIT text is rejected for SPDX-only packs' {
        $fixture = NewFixture
        Remove-Item -LiteralPath (Join-Path $fixture.DesktopPackPath 'LICENSE')
        WriteMetadata $fixture 'expression' 'MIT'
        $fixture.DesktopMitLicensePath = Join-Path $testRoot 'missing.txt'
        ExpectFailure { Copy-RuntimeNotices @fixture } '*Reviewed Desktop MIT text missing*'
    }
    Check 'Microsoft-specific license filenames are retained' {
        $fixture = NewFixture
        Remove-Item -LiteralPath (Join-Path $fixture.CorePackPath 'LICENSE.txt')
        WriteFixture (Join-Path $fixture.CorePackPath 'LICENSE-MSFT.TXT') '[TEST FIXTURE ONLY] Windows binary terms.'
        Copy-RuntimeNotices @fixture
        Assert (Test-Path -LiteralPath (Join-Path $fixture.DestinationDirectory 'microsoft.netcore.app.runtime.win-x64-10.0.14/LICENSE-MSFT.TXT')) 'Microsoft-specific license not copied.'
    }
    Write-Host "$script:passed passed; $script:failed failed. Synthetic offline runtime-pack fixtures only."
    if ($script:failed) { throw 'Packaging regression tests failed.' }
}
finally {
    # Delete only this test's newly created GUID directory inside the workspace.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $allowed = [IO.Path]::GetFullPath($base).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notmatch '^[a-f0-9]{32}$') { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
