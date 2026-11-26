<#
.SYNOPSIS
Lays out the Python the setup carries: python.org's own `python` package from nuget.org, unpacked into a folder.

.DESCRIPTION
The package (by the Python Software Foundation; the full Windows layout, not the embeddable one: python.exe, python312.dll, DLLs,
Lib with venv, ensurepip and pip, and LICENSE.txt, 41 MB) is pinned to one version and to the SHA-512 nuget.org lists for it, so a
changed or damaged download stops the build. The download is kept in artifacts\cache and fetched once; a cached file whose hash
is wrong is fetched again. The package's `tools` folder is written into -Destination, which is cleaned first. The repository holds
no Python binaries: this script is how they get into the setup (make-installer.ps1 calls it with python\runtime of the publish
folder). To move to another 3.12 release, change $version and $sha512 together: the hash is on
https://www.nuget.org/packages/python/<version> and in its catalog entry (packageHash, base64).

.EXAMPLE
pwsh tools\installer\fetch-python-runtime.ps1 -Destination artifacts\publish\app\python\runtime
#>
param(
    [Parameter(Mandatory)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# Python 3.12.10 is the last 3.12 release with binaries; nuget.org lists its package hash as this SHA-512 (base64).
$version = '3.12.10'
$sha512 = 'u9pNz2iKlCEbYtUJaKkbOPMF0LjR7NkCafdKhvigpPzrt8oWKgdTpHaR6z3wyWQAm9PYGUxv0Zr66NX9AeHMDw=='
$url = "https://api.nuget.org/v3-flatcontainer/python/$version/python.$version.nupkg"

function Get-PackageHash([string]$path) {
    $algorithm = [System.Security.Cryptography.SHA512]::Create()
    $stream = [System.IO.File]::OpenRead($path)
    try {
        return [Convert]::ToBase64String($algorithm.ComputeHash($stream))
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

$cache = Join-Path $root 'artifacts\cache'
New-Item -ItemType Directory -Force $cache | Out-Null
$package = Join-Path $cache "python.$version.nupkg"

if ((Test-Path $package) -and ((Get-PackageHash $package) -ne $sha512)) {
    Remove-Item $package -Force
}

if (-not (Test-Path $package)) {
    Write-Host "Fetching python $version from nuget.org..."
    $partial = "$package.part"
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $url -OutFile $partial -UseBasicParsing

    if ((Get-PackageHash $partial) -ne $sha512) {
        Remove-Item $partial -Force
        throw "The python $version package from nuget.org does not match its pinned SHA-512."
    }

    Move-Item $partial $package
}

if (Test-Path $Destination) {
    Remove-Item $Destination -Recurse -Force
}
New-Item -ItemType Directory -Force $Destination | Out-Null
$target = (Resolve-Path $Destination).Path.TrimEnd('\') + '\'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach ($entry in $zip.Entries) {
        if (-not $entry.FullName.StartsWith('tools/', [StringComparison]::Ordinal) -or $entry.FullName.EndsWith('/')) {
            continue
        }

        $file = [System.IO.Path]::GetFullPath((Join-Path $target $entry.FullName.Substring('tools/'.Length)))

        if (-not $file.StartsWith($target, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The package holds a path outside its folder: $($entry.FullName)"
        }

        New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $file, $true)
    }
}
finally {
    $zip.Dispose()
}

foreach ($needed in 'python.exe', 'python312.dll', 'LICENSE.txt', 'Lib\venv\__init__.py', 'Lib\site-packages\pip\__init__.py') {
    if (-not (Test-Path (Join-Path $Destination $needed))) {
        throw "The python $version package has no $needed."
    }
}

Write-Host "Python $version laid out in $Destination."
