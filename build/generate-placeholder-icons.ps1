# Generates two placeholder .ico files (tray.ico + app.ico) under src/.../Resources.
# Edit the DESIGN PARAMETERS block below, then run: pwsh build\generate-placeholder-icons.ps1
[CmdletBinding()]
param()

# ============================================================
# DESIGN PARAMETERS  ---  edit and re-run
# ============================================================

# Which two letters to render. Small one is drawn first (behind), large is drawn on top.
$SmallLetter = 'E'
$LargeLetter = 'L'

# Colors -- hex strings. The # is optional.
$BgHex    = '#221e1a'    # background
$SmallHex = '#FFFFFF'    # small letter (E)
$LargeHex = '#ffd900'    # large letter (L)

# Letter heights as a fraction of canvas size. 1.0 = full canvas height.
# Bumped values give bigger letters; the typographic font metrics still pad a bit either way.
$SmallSizeRatio = 0.75
$LargeSizeRatio = 1.2

# How much the large letter overlaps the small one's right edge, as a fraction of the
# small letter's width. 0 = no overlap (side-by-side), 0.5 = L covers half of E.
$OverlapRatio = 0.35

# Per-letter nudge, expressed as a fraction of canvas size so it scales between the 32px
# tray icon and the 256px app icon. Positive X = right, positive Y = down. Leave at 0 to
# use the auto-computed centered position.
$SmallOffsetX = 0.0
$SmallOffsetY = -0.2
$LargeOffsetX = 0.05
$LargeOffsetY = -0.1

# Output canvas sizes. Tray gets a small one (Windows scales 32 to 16 for the tray well),
# app gets a larger one (File Explorer shows up to 256 on thumbnails).
$TraySize = 32
$AppSize  = 256

# ============================================================

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function ConvertFrom-Hex {
    param([string]$Hex)
    $h = $Hex.TrimStart('#')
    if ($h.Length -ne 6) { throw "Bad hex color '$Hex' - expected 6 chars after the #." }
    $r = [Convert]::ToInt32($h.Substring(0, 2), 16)
    $g = [Convert]::ToInt32($h.Substring(2, 2), 16)
    $b = [Convert]::ToInt32($h.Substring(4, 2), 16)
    return [System.Drawing.Color]::FromArgb($r, $g, $b)
}

function New-PlaceholderIcon {
    param(
        [string]$Path,
        [int]$Size,
        [string]$SmallLetter,
        [string]$LargeLetter,
        [System.Drawing.Color]$BgColor,
        [System.Drawing.Color]$SmallColor,
        [System.Drawing.Color]$LargeColor,
        [single]$SmallSizeRatio,
        [single]$LargeSizeRatio,
        [single]$OverlapRatio,
        [single]$SmallOffsetX,
        [single]$SmallOffsetY,
        [single]$LargeOffsetX,
        [single]$LargeOffsetY
    )
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        $bgBrush = New-Object System.Drawing.SolidBrush($BgColor)
        $g.FillRectangle($bgBrush, 0, 0, $Size, $Size)
        $bgBrush.Dispose()

        $smallFontPx = [Math]::Floor($Size * $SmallSizeRatio)
        $largeFontPx = [Math]::Floor($Size * $LargeSizeRatio)

        $smallFont = New-Object System.Drawing.Font(
            'Segoe UI', [single]$smallFontPx,
            [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $largeFont = New-Object System.Drawing.Font(
            'Segoe UI', [single]$largeFontPx,
            [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)

        # GenericTypographic measures actual glyph bounds without the ~25% default font padding,
        # so overlap math acts on real letter widths instead of just trimming the padding.
        $tightFormat = [System.Drawing.StringFormat]::GenericTypographic
        $smallSize = $g.MeasureString($SmallLetter, $smallFont, [int]::MaxValue, $tightFormat)
        $largeSize = $g.MeasureString($LargeLetter, $largeFont, [int]::MaxValue, $tightFormat)

        $overlap = [single]($smallSize.Width * $OverlapRatio)
        $totalWidth = $smallSize.Width + $largeSize.Width - $overlap
        $startX = [single](($Size - $totalWidth) / 2)

        $largeY = [single](($Size - $largeSize.Height) / 2)
        $smallY = [single]($largeY + ($largeSize.Height - $smallSize.Height))

        # Auto position + user nudge (offsets are fractions of canvas so they scale tray vs app).
        $smallX = [single]($startX + $Size * $SmallOffsetX)
        $smallY = [single]($smallY + $Size * $SmallOffsetY)
        $largeX = [single]($startX + $smallSize.Width - $overlap + $Size * $LargeOffsetX)
        $largeY = [single]($largeY + $Size * $LargeOffsetY)

        $smallBrush = New-Object System.Drawing.SolidBrush($SmallColor)
        $largeBrush = New-Object System.Drawing.SolidBrush($LargeColor)
        try {
            $g.DrawString($SmallLetter, $smallFont, $smallBrush, $smallX, $smallY, $tightFormat)
            $g.DrawString($LargeLetter, $largeFont, $largeBrush, $largeX, $largeY, $tightFormat)
        } finally {
            $smallBrush.Dispose()
            $largeBrush.Dispose()
        }

        $smallFont.Dispose()
        $largeFont.Dispose()
    } finally {
        $g.Dispose()
    }

    $hIcon = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hIcon)
    $stream = [System.IO.File]::Create($Path)
    try { $icon.Save($stream) } finally { $stream.Close() }
    $bmp.Dispose()
    Write-Host "Wrote $Path ($Size x $Size)"
}

# Resolve hex strings into Colors here so the function signature stays typed.
$bg    = ConvertFrom-Hex $BgHex
$small = ConvertFrom-Hex $SmallHex
$large = ConvertFrom-Hex $LargeHex

$repoRoot = Split-Path -Parent $PSScriptRoot
$resourcesDir = Join-Path $repoRoot 'src\EmilsClaudeLaunchpad\Resources'
if (-not (Test-Path $resourcesDir)) {
    New-Item -ItemType Directory -Path $resourcesDir | Out-Null
}

$commonArgs = @{
    SmallLetter    = $SmallLetter
    LargeLetter    = $LargeLetter
    BgColor        = $bg
    SmallColor     = $small
    LargeColor     = $large
    SmallSizeRatio = $SmallSizeRatio
    LargeSizeRatio = $LargeSizeRatio
    OverlapRatio   = $OverlapRatio
    SmallOffsetX   = $SmallOffsetX
    SmallOffsetY   = $SmallOffsetY
    LargeOffsetX   = $LargeOffsetX
    LargeOffsetY   = $LargeOffsetY
}

New-PlaceholderIcon -Path (Join-Path $resourcesDir 'tray.ico') -Size $TraySize @commonArgs
New-PlaceholderIcon -Path (Join-Path $resourcesDir 'app.ico')  -Size $AppSize  @commonArgs
