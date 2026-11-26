<#
.SYNOPSIS
Builds the setup of OpenCAD2D, artifacts\installer\OpenCAD2D-Setup-<version>.exe.

.DESCRIPTION
Publishes src\OpenCAD2D.App self-contained for win-x64 in Release (the .NET runtime comes with it; not single-file and
not trimmed, since plugins, Python and the support folders find their files beside the exe) into artifacts\publish\app,
adds the Python the setup carries as python\runtime of that folder (tools\installer\fetch-python-runtime.ps1: python.org's
package from nuget.org, pinned by version and hash, kept in artifacts\cache), then compiles tools\installer\OpenCAD2D.iss
with Inno Setup 6. The version is OpenCAD2DVersion of Directory.Build.props.
ISCC.exe is looked for on the PATH, then in the Program Files and %LocalAppData%\Programs folders of Inno Setup 6; install
it with: winget install --id JRSoftware.InnoSetup -e --scope user

.EXAMPLE
pwsh tools\installer\make-installer.ps1
.EXAMPLE
pwsh tools\installer\make-installer.ps1 -Iscc 'D:\Tools\Inno Setup 6\ISCC.exe'
#>
param(
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
if ($props -notmatch '<OpenCAD2DVersion>([^<]+)</OpenCAD2DVersion>') {
    throw 'No OpenCAD2DVersion in Directory.Build.props.'
}
$version = $Matches[1].Trim()

if (-not $Iscc) {
    $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($found) {
        $Iscc = $found.Source
    }
    else {
        $folders = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:LocalAppData 'Programs')) |
            Where-Object { $_ } |
            ForEach-Object { Join-Path $_ 'Inno Setup 6\ISCC.exe' }
        $Iscc = $folders | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
}
if (-not $Iscc -or -not (Test-Path $Iscc)) {
    throw 'ISCC.exe of Inno Setup 6 not found. Install it (winget install --id JRSoftware.InnoSetup -e --scope user) or pass -Iscc <path>.'
}

# A folder cleaned first, so that a file the build no longer writes is not in the setup.
$publish = Join-Path $root 'artifacts\publish\app'
if (Test-Path $publish) {
    Remove-Item $publish -Recurse -Force
}

dotnet publish (Join-Path $root 'src\OpenCAD2D.App\OpenCAD2D.App.csproj') -c Release -r win-x64 --self-contained -warnaserror -nodeReuse:false -o $publish
if ($LASTEXITCODE -ne 0) {
    throw 'The publish failed.'
}

# The Python the setup carries, beside the opencad2d package the publish wrote into python: Python tools run on it where the
# machine has no Python of its own.
& (Join-Path $PSScriptRoot 'fetch-python-runtime.ps1') -Destination (Join-Path $publish 'python\runtime')

& $Iscc /Q "/DAppVersion=$version" "/DPublishDir=$publish" (Join-Path $PSScriptRoot 'OpenCAD2D.iss')
if ($LASTEXITCODE -ne 0) {
    throw 'The Inno Setup compiler failed.'
}

$setup = Join-Path $root "artifacts\installer\OpenCAD2D-Setup-$version.exe"
$megabytes = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "Setup written: $setup ($megabytes MB)"
