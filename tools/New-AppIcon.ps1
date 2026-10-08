Add-Type -AssemblyName PresentationCore, WindowsBase

$destination = Join-Path $PSScriptRoot '..\src\SleepTimer.App\Assets\sleep-timer.ico'
$destination = [System.IO.Path]::GetFullPath($destination)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null

function New-IconPng([int] $size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $dark = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(13, 24, 45))
    $moon = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(239, 220, 168))
    $cutout = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(13, 24, 45))
    $accent = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(133, 188, 206))
    $star = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(248, 235, 199))

    $margin = $size * 0.035
    $radius = $size * 0.23
    $context.DrawRoundedRectangle($dark, $null, [System.Windows.Rect]::new($margin, $margin, $size - 2 * $margin, $size - 2 * $margin), $radius, $radius)

    $moonCenter = [System.Windows.Point]::new($size * 0.43, $size * 0.37)
    $context.DrawEllipse($moon, $null, $moonCenter, $size * 0.24, $size * 0.24)
    $context.DrawEllipse($cutout, $null, [System.Windows.Point]::new($size * 0.53, $size * 0.15), $size * 0.21, $size * 0.21)

    foreach ($point in @(
        [System.Windows.Point]::new($size * 0.75, $size * 0.22),
        [System.Windows.Point]::new($size * 0.79, $size * 0.45),
        [System.Windows.Point]::new($size * 0.24, $size * 0.72)
    )) {
        $context.DrawEllipse($star, $null, $point, $size * 0.025, $size * 0.025)
    }

    $clockCenter = [System.Windows.Point]::new($size * 0.66, $size * 0.68)
    $context.DrawEllipse($accent, $null, $clockCenter, $size * 0.235, $size * 0.235)
    $context.DrawEllipse($dark, $null, $clockCenter, $size * 0.18, $size * 0.18)
    $pen = [System.Windows.Media.Pen]::new($moon, [Math]::Max(1.5, $size * 0.035))
    $pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $context.DrawLine($pen, $clockCenter, [System.Windows.Point]::new($size * 0.66, $size * 0.56))
    $context.DrawLine($pen, $clockCenter, [System.Windows.Point]::new($size * 0.75, $size * 0.72))
    $context.Close()

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    $encoder.Save($stream)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}

$sizes = @(16, 32, 48, 256)
$frames = foreach ($size in $sizes) { [pscustomobject]@{ Size = $size; Bytes = (New-IconPng $size) } }
$stream = [System.IO.File]::Create($destination)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
    $writer.Write($dimension)
    $writer.Write($dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write($frame.Bytes) }
$writer.Dispose()
$stream.Dispose()
$previewDestination = [System.IO.Path]::ChangeExtension($destination, '.png')
$previewFrame = $frames | Where-Object Size -eq 256 | Select-Object -First 1
[System.IO.File]::WriteAllBytes($previewDestination, $previewFrame.Bytes)
Write-Output "Created $destination"
Write-Output "Created $previewDestination"

