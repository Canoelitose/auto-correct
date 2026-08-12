<#
.SYNOPSIS
    Builds the AutoCorrect client packages.

.DESCRIPTION
    Two targets:
      FrameworkDependent  single AutoCorrect.exe, about 0.4 MB, needs the
                          .NET 8 Desktop Runtime on the target machine
      SelfContained       runs without any installed runtime, about 68 MB

    PublishTrimmed is not used: the .NET 8 SDK rejects trimming for WPF
    (NETSDK1168). See docs/BUILD.md.

.EXAMPLE
    pwsh build/publish.ps1 -Target Both -Test
#>
[CmdletBinding()]
param(
    [ValidateSet('FrameworkDependent', 'SelfContained', 'Both')]
    [string]$Target = 'Both',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src/AutoCorrect.App/AutoCorrect.App.csproj'
$testProject = Join-Path $repoRoot 'tests/AutoCorrect.Core.Tests/AutoCorrect.Core.Tests.csproj'
$artifacts = Join-Path $repoRoot 'artifacts'

if ($Test) {
    Write-Host 'Running core tests...' -ForegroundColor Cyan
    dotnet run --project $testProject -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed, aborting publish.' }
}

function Publish-Variant {
    param([string]$Name, [bool]$SelfContained)

    $outputDir = Join-Path $artifacts $Name
    Write-Host "Publishing $Name ($Runtime)..." -ForegroundColor Cyan

    $arguments = @(
        'publish', $appProject,
        '-c', 'Release',
        '-r', $Runtime,
        '--self-contained', $SelfContained.ToString().ToLowerInvariant(),
        '-o', $outputDir,
        '-p:PublishSingleFile=true',
        '-p:DebugType=none'
    )

    if ($SelfContained) {
        $arguments += '-p:EnableCompressionInSingleFile=true'
        $arguments += '-p:IncludeNativeLibrariesForSelfExtract=true'
    }

    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish of $Name failed." }

    $size = (Get-ChildItem $outputDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("  {0}: {1:N1} MB -> {2}" -f $Name, $size, $outputDir) -ForegroundColor Green
}

if ($Target -in @('FrameworkDependent', 'Both')) {
    Publish-Variant -Name 'client-framework-dependent' -SelfContained $false
}

if ($Target -in @('SelfContained', 'Both')) {
    Publish-Variant -Name 'client-self-contained' -SelfContained $true
}

Write-Host 'Done.' -ForegroundColor Green
