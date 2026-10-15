param([string]$out = (Join-Path $PSScriptRoot 'demo.dnest'))
# The demo job for the README screenshots: test1.dnest's settings with the demo parts, 2500 x 1250, mild steel 2 mm.
$ErrorActionPreference = 'Stop'
$demo = $PSScriptRoot
$p = Get-Content (Join-Path $PSScriptRoot '..\..\test-data\test1\test1.dnest') -Raw -Encoding utf8 | ConvertFrom-Json
$template = $p.DetailLoadInfos[0]
$rows = @()
foreach ($part in @(@('arm', 10), @('plate', 10), @('lever', 10), @('ring', 10), @('clip', 14), @('hub', 16))) {
  $row = $template | ConvertTo-Json -Depth 5 | ConvertFrom-Json
  $row.Path = Join-Path $demo ($part[0] + '.dxf')
  $row.Quantity = $part[1]
  $row.MirrorQuantity = 0
  $row.Spacing = -1
  $row.StrictAngle = 0
  $rows += $row
}

$p.DetailLoadInfos = $rows
$sheet = $p.SheetLoadInfos[0]
$sheet.Width = 2500
$sheet.Height = 1250
$sheet.Quantity = 2
$p.SheetLoadInfos = @($sheet)
$p.LastNestResultJson = ''
$p | Add-Member -NotePropertyName Material -NotePropertyValue 'black' -Force
$p | Add-Member -NotePropertyName Thickness -NotePropertyValue 2 -Force
$p | ConvertTo-Json -Depth 20 | Set-Content $out -Encoding utf8
"written $out"
