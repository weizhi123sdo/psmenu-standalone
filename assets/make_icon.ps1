# 生成 E:\code\psmenu\assets\app.ico
# 图形与 Shell.cs 的 MakeIcon() 同款：深色圆角方块 + 白色鼠标指针造型（按比例放大重画，16px 依旧清晰）
# ICO 结构：ICONDIR + 每尺寸一个 ICONDIRENTRY，全部为 32 位原始位图条目
#（256 想用 PNG 压缩条目，但实测 Explorer/ExtractAssociatedIcon 对 w=0 的 PNG 条目解析不出，
#  为了兼容性放弃 PNG，统一写原始位图：像素数据 + AND 掩码，dwBytesInRes = 像素 + 掩码）
# 可重跑：每次直接覆盖 app.ico

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outPath = Join-Path $PSScriptRoot 'app.ico'

# 按给定边长重画同款图形，返回「BITMAPINFOHEADER + 像素(BGRA 自下而上) + AND 掩码」的完整条目数据
function Get-EntryData([int]$size)
{
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $size / 32.0   # Shell.cs 画布是 32，这里等比放大

    # 圆角方块底 #26262A（与 MakeIcon 的 (38,38,42) 一致）
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 12 * $s
    $bg.AddArc(0, 0, $r, $r, 180, 90)
    $bg.AddArc($size - $r, 0, $r, $r, 270, 90)
    $bg.AddArc($size - $r, $size - $r, $r, $r, 0, 90)
    $bg.AddArc(0, $size - $r, $r, $r, 90, 90)
    $bg.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 38, 38, 42))), $bg)

    # 白色鼠标指针多边形（16px 下指针占比仍大，加描边保证轮廓）
    $pts = @(@(10,6),@(10,24),@(14,19),@(17,26),@(20,24),@(17,18),@(23,18)) |
        ForEach-Object { New-Object System.Drawing.PointF(($_[0] * $s), ($_[1] * $s)) }
    $g.FillPolygon([System.Drawing.Brushes]::White, $pts)
    $g.DrawPolygon((New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 38, 38, 42), [float](1.2 * $s))), $pts)
    $g.Dispose()

    # 取出 BGRA 像素（GDI 的 Scan0 是自下而上，要翻成 ICO 要的自下而上本来就是 ICO 的顺序，
    # 但 LockBits 给的是自上而下起始地址 + 负高度语义，这里直接按行翻转成 ICO 存储序）
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($size * $size * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    $stride = $size * 4
    $pixels = New-Object byte[] $bytes.Length
    for ($y = 0; $y -lt $size; $y++) {
        [Array]::Copy($bytes, ($size - 1 - $y) * $stride, $pixels, $y * $stride, $stride)
    }

    # AND 掩码：32 位自带 alpha 时全 0 即可，每行 1bit/px 按 4 字节对齐
    $maskRow = [int][Math]::Ceiling($size / 32.0) * 4
    $mask = New-Object byte[] ($maskRow * $size)

    # 组装条目：BITMAPINFOHEADER（biHeight = 实际高×2，因为 ICO 的高度含掩码）+ 像素 + 掩码
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]($pixels.Length + $mask.Length)); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    $bw.Write($pixels)
    $bw.Write($mask)
    $bw.Flush()
    $result = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return ,$result
}

# 256 用 0 表示（ICONDIRENTRY 单字节放不下 256）；从大到小排列
$sizes = @(256, 128, 64, 48, 32, 16)
$entries = @()
foreach ($sz in $sizes) {
    $data = Get-EntryData $sz
    $w = $(if ($sz -ge 256) { 0 } else { $sz })
    $entries += @{ W = $w; H = $w; Data = $data }
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICONDIR（保留 0、类型 1 图标、数量）
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$entries.Count)

# ICONDIRENTRY：宽/高/色板色数/保留 + 颜色平面/位深 + 数据长度/偏移
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $bw.Write([byte]$e.W); $bw.Write([byte]$e.H); $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Data.Length); $bw.Write([uint32]$offset)
    $offset += $e.Data.Length
}

foreach ($e in $entries) {
    $bw.Write($e.Data)
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
Write-Host ("OK  " + $outPath + "  (" + (Get-Item $outPath).Length + " bytes)")
