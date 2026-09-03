#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the Windows installer.

.DESCRIPTION
    Publishes win-x64 and compiles Pisum.Whisper.wxs into an MSI. The one command that turns a
    clean checkout into a Windows installer - the same command a person and a workflow run.

    Needs the WiX v6 .NET tool:  dotnet tool install --global wix --version 6.*

.PARAMETER Version
    The release version, without a leading 'v'. May carry a pre-release suffix (0.1.0-rc.1); the
    file name keeps it and the MSI's own ProductVersion does not - see below.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$root = Resolve-Path (Join-Path $scriptDir '..' '..')

# The payload is published beside the .wxs rather than under artifacts/, and
# packaging/windows/.gitignore is what keeps 130 MB untracked. Both this path and the ARP icon reach
# wix as absolute -define values: WiX resolves a SourceFile against the current directory and a
# <Files> pattern against the current directory plus the Directory/@Name chain, never against the
# .wxs, so relative paths here built only when the current directory happened to be this one.
$publishDir = Join-Path $scriptDir 'publish'
$iconFile = Join-Path $root 'packaging' 'icon' 'app-icon.ico'
$outputDir = Join-Path $root 'artifacts'
$msi = Join-Path $outputDir "Pisum.Whisper_${Version}_win-x64.msi"

# Windows Installer's ProductVersion is major.minor.build and nothing else: a pre-release suffix is
# a link error, not a warning. The full string still reaches the file name, and on the other
# platform the Info.plist and the cask, so a release's artifacts are named alike even when the
# number inside the MSI cannot carry the suffix.
$msiVersion = ($Version -split '-')[0]

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# Self-contained and ReadyToRun; not single-file and not trimmed (design D1).
dotnet publish (Join-Path $root 'src' 'Pisum.Whisper.App') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:Version=$Version `
    --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Design D2: 84 MB of libSkiaSharp.pdb and 21 MB of libHarfBuzzSharp.pdb, native symbol files for
# third-party C++ that nobody here will ever load into a debugger. Deleting them takes the payload
# from 228 MB to ~128 MB. The three managed .pdb files stay: they are 0.2 MB, and a logged stack
# trace from an installed build is unactionable without line numbers.
foreach ($pdb in 'libSkiaSharp.pdb', 'libHarfBuzzSharp.pdb') {
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $publishDir $pdb)
}

$payloadMb = [math]::Round((Get-ChildItem -Recurse -File $publishDir | Measure-Object -Property Length -Sum).Sum / 1MB, 1)

wix build `
    (Join-Path $scriptDir 'Pisum.Whisper.wxs') `
    -define "Version=$msiVersion" `
    -define "PublishDir=$publishDir" `
    -define "IconFile=$iconFile" `
    -arch x64 `
    -out $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed with exit code $LASTEXITCODE" }

Write-Host "Created: $msi"
Write-Host "  Version:        $Version  (MSI ProductVersion $msiVersion)"
Write-Host "  Payload:        $payloadMb MB"
Write-Host "  Installer size: $([math]::Round((Get-Item $msi).Length / 1MB, 1)) MB"
