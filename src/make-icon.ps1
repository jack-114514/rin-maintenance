param([string]$OutputPath=(Join-Path $PSScriptRoot 'manager.ico'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$frames=@()
foreach($size in @(16,24,32,48,64,128,256)){
    $bitmap=New-Object Drawing.Bitmap $size,$size
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($size/256.0),($size/256.0))
    $graphics.Clear([Drawing.Color]::Transparent)
    $blue=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(29,111,181))
    $green=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(26,162,114))
    $white=New-Object Drawing.SolidBrush ([Drawing.Color]::White)
    $graphics.FillEllipse($blue,4,4,248,248)
    $bubble=New-Object Drawing.Drawing2D.GraphicsPath
    $bubble.AddArc(46,47,45,45,180,90);$bubble.AddArc(162,47,45,45,270,90)
    $bubble.AddArc(162,131,45,45,0,90);$bubble.AddLine(103,176,68,201);$bubble.AddLine(68,201,74,173)
    $bubble.AddArc(46,131,45,45,90,90);$bubble.CloseFigure()
    $graphics.FillPath($white,$bubble)
    foreach($x in @(82,118,154)){$graphics.FillEllipse($blue,$x,99,15,15)}
    $shield=New-Object Drawing.Drawing2D.GraphicsPath
    $shield.AddPolygon([Drawing.PointF[]]@((New-Object Drawing.PointF 190,140),(New-Object Drawing.PointF 231,157),(New-Object Drawing.PointF 225,206),(New-Object Drawing.PointF 190,232),(New-Object Drawing.PointF 155,206),(New-Object Drawing.PointF 149,157)))
    $outline=New-Object Drawing.Pen ([Drawing.Color]::White),9
    $graphics.DrawPath($outline,$shield);$graphics.FillPath($green,$shield)
    $tick=New-Object Drawing.Pen ([Drawing.Color]::White),10
    $tick.StartCap=[Drawing.Drawing2D.LineCap]::Round;$tick.EndCap=[Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLines($tick,[Drawing.PointF[]]@((New-Object Drawing.PointF 170,184),(New-Object Drawing.PointF 185,198),(New-Object Drawing.PointF 211,170)))
    # Use classic 32-bit DIB frames. Windows shell and .NET Framework WinForms
    # both decode them, including the 256px frame, without PNG-icon ambiguity.
    $stream=New-Object IO.MemoryStream
    $frameWriter=New-Object IO.BinaryWriter $stream
    $maskStride=[int]([Math]::Ceiling($size/32.0)*4)
    $frameWriter.Write([int]40);$frameWriter.Write([int]$size);$frameWriter.Write([int]($size*2))
    $frameWriter.Write([uint16]1);$frameWriter.Write([uint16]32);$frameWriter.Write([int]0)
    $frameWriter.Write([int]($size*$size*4+$maskStride*$size))
    foreach($zero in 1..4){$frameWriter.Write([int]0)}
    $rectangle=New-Object Drawing.Rectangle 0,0,$size,$size
    $locked=$bitmap.LockBits($rectangle,[Drawing.Imaging.ImageLockMode]::ReadOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try{
        $pixels=New-Object byte[] ([Math]::Abs($locked.Stride)*$size)
        [Runtime.InteropServices.Marshal]::Copy($locked.Scan0,$pixels,0,$pixels.Length)
        for($y=$size-1;$y -ge 0;$y--){$frameWriter.Write($pixels,($y*[Math]::Abs($locked.Stride)),($size*4))}
    }finally{$bitmap.UnlockBits($locked)}
    $mask=New-Object byte[] ($maskStride*$size)
    $frameWriter.Write($mask);$frameWriter.Flush()
    $frames+=,@{Size=$size;Bytes=$stream.ToArray()}
    $tick.Dispose();$outline.Dispose();$shield.Dispose();$bubble.Dispose();$blue.Dispose();$green.Dispose();$white.Dispose();$graphics.Dispose();$bitmap.Dispose();$frameWriter.Dispose();$stream.Dispose()
}
$file=[IO.File]::Create($OutputPath)
$writer=New-Object IO.BinaryWriter $file
try{
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset=6+16*$frames.Count
    foreach($frame in $frames){
        $dimension=if($frame.Size -eq 256){0}else{$frame.Size}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset)
        $offset+=$frame.Bytes.Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
}finally{$writer.Dispose()}
