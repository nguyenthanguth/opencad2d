<#
.SYNOPSIS
Builds the core library, GeometryHelper, in Release and copies it into Lib\.

.DESCRIPTION
OpenCAD2D uses GeometryHelper (geometry, DXF and DWG) as a binary: Lib\GeometryHelper.dll with its XML documentation
(its PDB is embedded in the DLL). This script builds the GeometryHelper repository, by default the clone beside this
one (..\GeometryHelper), copies the two files into Lib\ and writes Lib\VERSION.txt with the version and the commit the
DLL was built from. A clone with changes not committed is refused unless -AllowDirty is given, so that VERSION.txt
always names the source of the DLL.

.EXAMPLE
pwsh tools\update-lib.ps1
.EXAMPLE
pwsh tools\update-lib.ps1 -GeometryHelper D:\GitHub\GeometryHelper -AllowDirty
#>
param(
    [string]$GeometryHelper = (Join-Path $PSScriptRoot '..\..\GeometryHelper'),
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = (Resolve-Path $GeometryHelper).Path
$project = Join-Path $source 'src\GeometryHelper\GeometryHelper.csproj'
if (-not (Test-Path $project)) {
    throw "No GeometryHelper project at $project. Pass -GeometryHelper <folder of the GeometryHelper clone>."
}

$commit = (git -C $source rev-parse --short=8 HEAD).Trim()
$dirty = git -C $source status --porcelain -- src/GeometryHelper
if ($dirty -and -not $AllowDirty) {
    throw "src/GeometryHelper has changes not committed in $source. Commit them, or pass -AllowDirty for a trial copy."
}

dotnet build $project -c Release -warnaserror --no-incremental -nodeReuse:false
if ($LASTEXITCODE -ne 0) {
    throw "The GeometryHelper build failed."
}

$bin = Join-Path $source 'src\GeometryHelper\bin\Release\netstandard2.0'
$lib = Join-Path $root 'Lib'
New-Item -ItemType Directory -Force $lib | Out-Null
Copy-Item (Join-Path $bin 'GeometryHelper.dll'), (Join-Path $bin 'GeometryHelper.xml') $lib -Force

$version = ((Get-Item (Join-Path $lib 'GeometryHelper.dll')).VersionInfo.ProductVersion -split '\+')[0]
$note = if ($dirty) { ' (with changes not committed)' } else { '' }
$text = "GeometryHelper $version, built in Release from commit $commit$note of https://github.com/nguyenthanguth/GeometryHelper`n"
[System.IO.File]::WriteAllText((Join-Path $lib 'VERSION.txt'), $text, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Lib updated: $text"
