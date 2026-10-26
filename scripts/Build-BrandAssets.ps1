param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
foreach ($item in @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))) {
    $size = [int]$item[1]
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $ink = [Windows.Media.BrushConverter]::new().ConvertFromString('#192E35')
    $orange = [Windows.Media.BrushConverter]::new().ConvertFromString('#D7794D')
    $context.DrawRectangle($ink, $null, [Windows.Rect]::new(0, 0, $size, $size))
    $scale = $size / 150.0
    $context.PushTransform([Windows.Media.ScaleTransform]::new($scale, $scale))
    $context.DrawRectangle($null, [Windows.Media.Pen]::new($orange, 5), [Windows.Rect]::new(60, 26, 55, 76))
    $context.DrawRectangle($ink, [Windows.Media.Pen]::new([Windows.Media.Brushes]::White, 5), [Windows.Rect]::new(35, 48, 55, 76))
    foreach ($line in @(70, 85, 100)) { $context.DrawLine([Windows.Media.Pen]::new([Windows.Media.Brushes]::White, 4), [Windows.Point]::new(47, $line), [Windows.Point]::new(78, $line)) }
    $context.Pop()
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $OutputDirectory ($item[0] + '.png')))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}
