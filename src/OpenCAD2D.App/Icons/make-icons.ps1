# Writes the icons of OpenCAD2D from the vector drawing below: app.ico, the app's (ApplicationIcon and the window's),
# and drawing-file.ico, the icon of a .dxf or .dwg file associated with the app (--register), and icon.png, the 128-pixel
# icon of the NuGet packages. Each .ico holds 16, 24, 32, 48 and 256 pixels, drawn from one drawing 256 units square. The 256 frame is a PNG, the smaller ones 32-bit bitmaps. Run it with
# Windows PowerShell from any folder after changing a shape:
#   powershell -NoProfile -ExecutionPolicy Bypass -File src\OpenCAD2D.App\Icons\make-icons.ps1
#
# The mark (concept E3 of the design canvas, 2026-11-23): on a white rounded tile, a blue O holds a C opening to the right,
# and the C holds the letters AD, so it reads O, C, AD. The letters are outlines of Space Grotesk Bold (SIL Open Font
# License), size 50 and spacing -2, placed so that their ink sits in the middle of the room the C leaves: 26.05 units from
# the inside of the C on the left and from the inside of the O on the right, and centred at 128 up and down. No font is
# needed to run this script. Below 24 pixels the letters cannot be read and are left out: the O and the C stay.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$blue = '#1F6FD6'
$tile = '#FFFFFF'
$tileEdge = '#D5DBE3'
$paper = '#FFFFFF'
$paperEdge = '#C3CCD7'
$fold = '#D5DBE3'

# The mark, in units of 256: the tile, the O (a circle stroked), the C (an arc stroked) and the letters AD (filled).
$tileRect = '18,18,220,220'
$tileRadius = 42
$ring = @(128, 128, 84, 24)                               # centre x, y, radius, width
$cArc = 'M165.2,94.5 A50,50 0 1 0 165.2,161.5'
$cWidth = 20
$letters = 'M114.05 145.5 123.25 110.5H134.75L143.95 145.5H137.15L135.25 137.8H122.75L120.85 145.5ZM124.3 131.7H133.7L129.45 114.65H128.55Z ' +
    'M145.15 145.5V139.7H149.75V116.3H145.15V110.5H159.55Q166.6 110.5 170.28 114.08Q173.95 117.65 173.95 124.7V131.3Q173.95 138.35 170.28 141.93Q166.6 145.5 159.55 145.5ZM156.35 139.5H159.65Q163.65 139.5 165.5 137.4Q167.35 135.3 167.35 131.5V124.5Q167.35 120.65 165.5 118.58Q163.65 116.5 159.65 116.5H156.35Z'

# The file (option F1 of the design canvas, 2026-11-24): the app's tile with its top right corner folded over like a page,
# the mark on it at 0.86 of its size, centred at 128 across and 134 down so it clears the fold. Square, so the mark stays
# large in Explorer at 16 pixels; the O and the C are drawn thicker at small sizes (160 / size units more) to stay solid.
$page = 'M60,18 L182,18 L238,74 L238,196 Q238,238 196,238 L60,238 Q18,238 18,196 L18,60 Q18,18 60,18 Z'
$pageFold = 'M182,18 L182,58 Q182,74 198,74 L238,74 Z'
$markOnPage = @(0.86, 17.92, 23.92)                       # scale, then x and y added after scaling

function Get-Brush($hex) { [System.Windows.Media.BrushConverter]::new().ConvertFromString($hex) }

function Write-Mark($dc, $size, $withTile, $thicken = 0) {
    $pen = [System.Windows.Media.Pen]::new((Get-Brush $blue), $ring[3] + $thicken)
    if ($withTile) {
        $edge = [System.Windows.Media.Pen]::new((Get-Brush $tileEdge), 4)
        $dc.DrawRoundedRectangle((Get-Brush $tile), $edge, [System.Windows.Rect]::Parse($tileRect), $tileRadius, $tileRadius)
    }
    $dc.DrawEllipse($null, $pen, [System.Windows.Point]::new($ring[0], $ring[1]), $ring[2], $ring[2])
    $cPen = [System.Windows.Media.Pen]::new((Get-Brush $blue), $cWidth + $thicken)
    $dc.DrawGeometry($null, $cPen, [System.Windows.Media.Geometry]::Parse($cArc))
    if ($size -ge 24) {
        $dc.DrawGeometry((Get-Brush $blue), $null, [System.Windows.Media.Geometry]::Parse($letters))
    }
}

function Get-Rendering($size, $isDocument) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $dc = $visual.RenderOpen()
    $dc.PushTransform([System.Windows.Media.ScaleTransform]::new($size / 256, $size / 256))

    if ($isDocument) {
        $line = [Math]::Max(256 / $size, 4)
        $pen = [System.Windows.Media.Pen]::new((Get-Brush $paperEdge), $line)
        $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
        $dc.DrawGeometry((Get-Brush $paper), $pen, [System.Windows.Media.Geometry]::Parse($page))
        $dc.DrawGeometry((Get-Brush $fold), $pen, [System.Windows.Media.Geometry]::Parse($pageFold))
        $group = [System.Windows.Media.TransformGroup]::new()
        $group.Children.Add([System.Windows.Media.ScaleTransform]::new($markOnPage[0], $markOnPage[0]))
        $group.Children.Add([System.Windows.Media.TranslateTransform]::new($markOnPage[1], $markOnPage[2]))
        $dc.PushTransform($group)
        Write-Mark $dc $size $false (160 / $size)
        $dc.Pop()
    }
    else {
        Write-Mark $dc $size $true
    }

    $dc.Pop()
    $dc.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    return [System.Windows.Media.Imaging.FormatConvertedBitmap]::new($bitmap, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
}

function Get-FrameBytes($bitmap, $size) {
    if ($size -ge 256) {
        $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $stream = [System.IO.MemoryStream]::new()
        $encoder.Save($stream)
        return , $stream.ToArray()
    }

    # A bitmap of the icon format: its header with the height doubled, the rows bottom up, then an AND mask left clear.
    $stride = $size * 4
    $pixels = New-Object byte[] ($stride * $size)
    $bitmap.CopyPixels($pixels, $stride, 0)
    $maskStride = [int][Math]::Ceiling($size / 32) * 4
    $stream = [System.IO.MemoryStream]::new()
    $w = [System.IO.BinaryWriter]::new($stream)
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($stride * $size + $maskStride * $size)); $w.Write([int]0); $w.Write([int]0)
    $w.Write([int]0); $w.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $stride, $stride) }
    $w.Write((New-Object byte[] ($maskStride * $size)))
    $w.Flush()
    return , $stream.ToArray()
}

function Write-Icon($path, $isDocument) {
    $sizes = 16, 24, 32, 48, 256
    $frames = foreach ($size in $sizes) { , (Get-FrameBytes (Get-Rendering $size $isDocument) $size) }
    $stream = [System.IO.File]::Create($path)
    $w = [System.IO.BinaryWriter]::new($stream)
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $across = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$across); $w.Write([byte]$across); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$frames[$i].Length); $w.Write([int]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $w.Write($frame) }
    $w.Close()
    Write-Output ("Wrote " + $path + " (" + (Get-Item $path).Length + " bytes)")
}

Write-Icon (Join-Path $PSScriptRoot 'app.ico') $false
Write-Icon (Join-Path $PSScriptRoot 'drawing-file.ico') $true

# The icon of the NuGet packages (PackageIcon): the app's mark, 128 pixels, as nuget.org shows it.
$png = Join-Path $PSScriptRoot 'icon.png'
[System.IO.File]::WriteAllBytes($png, (Get-FrameBytes (Get-Rendering 128 $false) 256))
Write-Output ("Wrote " + $png + " (" + (Get-Item $png).Length + " bytes)")
