<#
.SYNOPSIS
    Builds the unsigned Microsoft Store bundle of Atap for upload in Partner Center.

.DESCRIPTION
    Takes the Store builds of Atap (dotnet publish -p:AtapChannel=Store) for win-x64 and win-arm64.
    Adds AppxManifest.xml and the Store logos from assets/brand/store, then packs one .msix per
    architecture with MakeAppx and bundles them into one .msixbundle. The bundle is not signed.
    The Store signs it on submission.

    Needs makeappx.exe and makepri.exe from the Windows SDK. The GitHub Windows runners have it.
    The release workflow runs this script. See docs/RELEASING.md.

.EXAMPLE
    ./packaging/msix/build.ps1 -Version 0.1.0 -PublishRoot store-publish -OutputDir store
#>
[CmdletBinding()]
param(
    # Stable version without the v, for example 0.1.0. The package version becomes 0.1.0.0.
    [Parameter(Mandatory)] [string] $Version,

    # Folder with one Store build per runtime, in win-x64 and win-arm64.
    [Parameter(Mandatory)] [string] $PublishRoot,

    # The bundle is written here. Its work folder is removed when the bundle is built.
    [Parameter(Mandatory)] [string] $OutputDir,

    [string] $Manifest = (Join-Path $PSScriptRoot 'AppxManifest.xml'),

    [string] $AssetsDir = (Join-Path $PSScriptRoot '..\..\assets\brand\store'),

    # Folder with makeappx.exe and makepri.exe. Left out, the newest Windows SDK is used.
    [string] $SdkBinDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullPath([string] $Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Invoke-Tool([string] $Path, [string[]] $Arguments) {
    & $Path @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path -Leaf $Path) failed with exit code $LASTEXITCODE."
    }
}

# MSIX versions are four numbers from 0 to 65535. The Store needs the last one to be 0.
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Version '$Version' is not X.Y.Z. Store packages are built for stable versions only."
}
if (@($Version -split '\.' | Where-Object { [long]$_ -gt 65535 }).Count -gt 0) {
    throw "Version '$Version' has a part above 65535, which MSIX does not allow."
}
$packageVersion = "$Version.0"

$PublishRoot = Get-FullPath $PublishRoot
$OutputDir = Get-FullPath $OutputDir
$Manifest = Get-FullPath $Manifest
$AssetsDir = Get-FullPath $AssetsDir

$template = Get-Content -LiteralPath $Manifest -Raw -Encoding utf8
$unfilled = @([regex]::Matches($template, '\[(PACKAGE-IDENTITY-NAME|PUBLISHER-CN|PUBLISHER-DISPLAY-NAME)\]') |
    ForEach-Object { $_.Value } | Select-Object -Unique)
if ($unfilled.Count -gt 0) {
    throw "Fill in $($unfilled -join ', ') in $Manifest with the values from Partner Center first."
}

$logos = @(Get-ChildItem -LiteralPath $AssetsDir -Filter '*.png' -File)
if ($logos.Count -eq 0) {
    throw "No Store logos found in $AssetsDir."
}

# Runtime of the Store build, and the MSIX architecture it becomes.
$targets = [ordered]@{ 'win-x64' = 'x64'; 'win-arm64' = 'arm64' }

$work = Join-Path $OutputDir 'work'
$packagesDir = Join-Path $work 'packages'
if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $packagesDir | Out-Null

# One folder per architecture, laid out the way the package installs.
$layouts = [ordered]@{}
foreach ($rid in $targets.Keys) {
    $arch = $targets[$rid]
    $source = Join-Path $PublishRoot $rid
    if (-not (Test-Path -LiteralPath (Join-Path $source 'Atap.exe'))) {
        throw "No Store build found in $source."
    }
    # The Store updates Atap, so a Store build must not carry Velopack.
    if (Test-Path -LiteralPath (Join-Path $source 'Velopack.dll')) {
        throw "$source has Velopack.dll. Publish it with -p:AtapChannel=Store."
    }
    $built = ((Get-Item -LiteralPath (Join-Path $source 'Atap.dll')).VersionInfo.ProductVersion -split '\+')[0]
    if ($built -ne $Version) {
        throw "The Store build in $source is version '$built', not '$Version'."
    }

    $layout = Join-Path $work "layout-$arch"
    Copy-Item -LiteralPath $source -Destination $layout -Recurse
    # Left out like vpk pack does: debug symbols and the .NET crash dump helper.
    # A Where-Object filter, because -Include with -LiteralPath is ignored in Windows PowerShell 5.1.
    Get-ChildItem -LiteralPath $layout -Recurse -File |
        Where-Object { $_.Extension -eq '.pdb' -or $_.Name -eq 'createdump.exe' } |
        Remove-Item

    $assets = Join-Path $layout 'Assets'
    New-Item -ItemType Directory -Force -Path $assets | Out-Null
    $logos | Copy-Item -Destination $assets

    $text = $template.Replace('[VERSION]', $packageVersion).Replace('[ARCH]', $arch)
    [IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $text, [Text.UTF8Encoding]::new($false))
    $layouts[$arch] = $layout
}

if (-not $SdkBinDir) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $SdkBinDir = Get-ChildItem -LiteralPath $kits -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64' } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'makeappx.exe') } |
        Select-Object -First 1
}
if (-not $SdkBinDir) {
    throw 'makeappx.exe was not found. Install the Windows SDK, or pass -SdkBinDir.'
}
$makeAppx = Join-Path $SdkBinDir 'makeappx.exe'
$makePri = Join-Path $SdkBinDir 'makepri.exe'
Write-Output "Windows SDK tools: $SdkBinDir"

# resources.pri lets Windows pick the scale-100 or scale-200 logo. It indexes the logos only, not the
# app files. Resource packs are turned off, so each architecture stays one package.
$priRoot = Join-Path $work 'pri'
$priAssets = Join-Path $priRoot 'Assets'
New-Item -ItemType Directory -Force -Path $priAssets | Out-Null
$logos | Copy-Item -Destination $priAssets
$priConfig = Join-Path $work 'priconfig.xml'
Invoke-Tool $makePri @('createconfig', '/cf', $priConfig, '/dq', 'en-US', '/o')
$config = [xml](Get-Content -LiteralPath $priConfig -Raw)
foreach ($node in @($config.DocumentElement.ChildNodes | Where-Object { $_.LocalName -eq 'packaging' })) {
    [void]$config.DocumentElement.RemoveChild($node)
}
$config.Save($priConfig)

foreach ($arch in $layouts.Keys) {
    $layout = $layouts[$arch]
    Invoke-Tool $makePri @(
        'new', '/pr', $priRoot, '/cf', $priConfig,
        '/mn', (Join-Path $layout 'AppxManifest.xml'),
        '/of', (Join-Path $layout 'resources.pri'), '/o')
    Invoke-Tool $makeAppx @(
        'pack', '/d', $layout,
        '/p', (Join-Path $packagesDir "Atap_${packageVersion}_$arch.msix"), '/o')
}

$bundle = Join-Path $OutputDir "Atap_${packageVersion}_x64_arm64.msixbundle"
Invoke-Tool $makeAppx @('bundle', '/d', $packagesDir, '/p', $bundle, '/bv', $packageVersion, '/o')

Remove-Item -LiteralPath $work -Recurse -Force
Write-Output "Built $bundle"
