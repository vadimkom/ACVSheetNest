# Draws the made-up demo job the README screenshots show: an arm, a plate, a lever, a ring, a clip and a hub,
# in millimetres. Run from anywhere: .\assets\demo\make-demo.ps1 writes the DXFs next to itself.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$inv = [System.Globalization.CultureInfo]::InvariantCulture
function F($v) { ([double]$v).ToString('0.######', $inv) }

function Save($name, [string[]]$entities) {
  $text = @('0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '4', '0', 'ENDSEC', '0', 'SECTION', '2', 'ENTITIES') + $entities + @('0', 'ENDSEC', '0', 'EOF')
  [IO.File]::WriteAllLines((Join-Path $here "$name.dxf"), $text)
}
function Line($x1, $y1, $x2, $y2) { @('0', 'LINE', '8', '0', '10', (F $x1), '20', (F $y1), '30', '0', '11', (F $x2), '21', (F $y2), '31', '0') }
function Circle($x, $y, $r) { @('0', 'CIRCLE', '8', '0', '10', (F $x), '20', (F $y), '30', '0', '40', (F $r)) }
function Arc($x, $y, $r, $from, $to) { @('0', 'ARC', '8', '0', '10', (F $x), '20', (F $y), '30', '0', '40', (F $r), '50', (F $from), '51', (F $to)) }
function Polygon([double[]]$pts) {
  $out = @()
  for ($i = 0; $i -lt $pts.Count; $i += 2) {
    $j = ($i + 2) % $pts.Count
    $out += Line $pts[$i] $pts[$i + 1] $pts[$j] $pts[$j + 1]
  }
  $out
}
function Polar($cx, $cy, $r, $deg) { $a = $deg * [Math]::PI / 180; @(($cx + $r * [Math]::Cos($a)), ($cy + $r * [Math]::Sin($a))) }

# Plate: 300 x 200 with a fixing hole in each corner.
Save 'plate' ((Polygon @(0, 0, 300, 0, 300, 200, 0, 200)) + (Circle 25 25 7) + (Circle 275 25 7) + (Circle 25 175 7) + (Circle 275 175 7))

# Ring: an open ring, R160 outside, R70 inside, with a tapered slot at the top.
$o1 = Polar 160 160 160 80; $i1 = Polar 160 160 70 80; $o2 = Polar 160 160 160 100; $i2 = Polar 160 160 70 100
Save 'ring' ((Arc 160 160 160 100 80) + (Arc 160 160 70 100 80) + (Line $o1[0] $o1[1] $i1[0] $i1[1]) + (Line $o2[0] $o2[1] $i2[0] $i2[1]))

# Hub: a R45 disc with a centre bore and four bolt holes; it fits the ring's middle.
$hub = (Circle 45 45 45) + (Circle 45 45 10)
foreach ($deg in 45, 135, 225, 315) { $p = Polar 45 45 25 $deg; $hub += Circle $p[0] $p[1] 7 }
Save 'hub' $hub

# Clip: a 150 x 50 slot with a hole at one end.
Save 'clip' ((Line 25 0 125 0) + (Arc 125 25 25 270 90) + (Line 125 50 25 50) + (Arc 25 25 25 90 270) + (Circle 25 25 14))

# Lever: tapers to a forked end, a hole at the wide end.
Save 'lever' ((Polygon @(0, 0, 150, 15, 190, 0, 175, 25, 190, 50, 150, 35, 0, 50)) + (Circle 25 25 12))

# Arm: a curved bar, R270-R330 over 70 degrees, with five holes along it.
$a1 = Polar 0 0 330 10; $b1 = Polar 0 0 270 10; $a2 = Polar 0 0 330 80; $b2 = Polar 0 0 270 80
$arm = (Arc 0 0 330 10 80) + (Arc 0 0 270 10 80) + (Line $a1[0] $a1[1] $b1[0] $b1[1]) + (Line $a2[0] $a2[1] $b2[0] $b2[1])
foreach ($deg in 18, 30, 42, 54, 66) { $p = Polar 0 0 300 $deg; $arm += Circle $p[0] $p[1] 9 }
Save 'arm' $arm

Get-ChildItem $here -Filter *.dxf | ForEach-Object { $_.Name }
