param([string]$OutputPath = (Join-Path $PSScriptRoot "..\assets\player_car.png"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$w = 256
$h = 160
$bmp = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
$g.Clear([System.Drawing.Color]::Transparent)

$shadow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(51, 0, 0, 0))
$black = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 17, 17, 17))
$grayWheel = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 68, 68, 68))
$bodyRed = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 176, 58, 46))
$darkRed = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 146, 43, 33))
$outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 59, 31, 26), 4)
$bumper = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 127, 140, 141))
$bumperPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 44, 62, 80), 3)
$window = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 27, 38, 49))
$windowPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 11, 15, 20), 3)
$light = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 231, 76, 60))
$lightY = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 245, 176, 65))
$plate = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 244, 246, 247))
$plateInk = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 44, 62, 80))
$highlight = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 217, 136, 128))

$g.FillEllipse($shadow, 24, 132, 208, 20)
$g.FillRectangle($black, 36, 108, 34, 34)
$g.FillRectangle($black, 186, 108, 34, 34)
$g.FillRectangle($grayWheel, 44, 116, 18, 18)
$g.FillRectangle($grayWheel, 194, 116, 18, 18)

$bodyRect = [System.Drawing.Rectangle]::new(28, 64, 200, 64)
$g.FillRectangle($bodyRed, $bodyRect)
$g.DrawRectangle($outline, $bodyRect)

$g.FillRectangle($bumper, 34, 120, 188, 22)
$g.DrawRectangle($bumperPen, 34, 120, 188, 22)

$roof = [System.Drawing.Drawing2D.GraphicsPath]::new()
$roof.AddPolygon([System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(64, 26),
    [System.Drawing.Point]::new(192, 26),
    [System.Drawing.Point]::new(216, 68),
    [System.Drawing.Point]::new(40, 68)
))
$g.FillPath($darkRed, $roof)
$g.DrawPath($outline, $roof)

$win = [System.Drawing.Drawing2D.GraphicsPath]::new()
$win.AddPolygon([System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(80, 34),
    [System.Drawing.Point]::new(176, 34),
    [System.Drawing.Point]::new(192, 60),
    [System.Drawing.Point]::new(64, 60)
))
$g.FillPath($window, $win)
$g.DrawPath($windowPen, $win)
$g.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 46, 64, 83)), 88, 38, 80, 6)

$g.FillRectangle($light, 30, 76, 34, 18)
$g.FillRectangle($light, 192, 76, 34, 18)
$g.FillRectangle($lightY, 36, 82, 22, 6)
$g.FillRectangle($lightY, 198, 82, 22, 6)

$g.FillRectangle($plate, 96, 96, 64, 18)
$g.DrawRectangle($bumperPen, 96, 96, 64, 18)
$g.FillRectangle($plateInk, 108, 102, 40, 5)

$g.FillRectangle($highlight, 42, 66, 172, 4)

$out = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($out)) | Out-Null
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Get-Item $out | Select-Object Name, Length, LastWriteTime
