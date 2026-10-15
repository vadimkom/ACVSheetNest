param(
  [string]$project = (Join-Path $PSScriptRoot 'demo.dnest'),
  [string]$outDir = (Join-Path $PSScriptRoot '..\..\imgs'),
  [string]$language = 'en',
  [int]$width = 1614,
  [int]$height = 1007)
# Opens the demo job (make-demo.ps1, then make-project.ps1) in the Release build, in English by default, nests it
# and takes the README's screenshots into imgs\. -language bg -width 1080 -height 1850 checks another language or
# a portrait screen instead. The program's language is put back as it was afterwards.
# The window draws itself into each picture, so whatever else is on the screen stays out of it.
$ErrorActionPreference = 'Stop'
$s = $PSScriptRoot
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W4 { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
public struct R { public int L, T, Ri, B; } }
"@
$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function Cond($prop, $v) { New-Object System.Windows.Automation.PropertyCondition($prop, $v) }
function ByName($name) { $main.FindFirst($TS::Descendants, (Cond $A::NameProperty $name)) }
function ById($id) { $main.FindFirst($TS::Descendants, (Cond $A::AutomationIdProperty $id)) }
function Press($el) {
  $o = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$o)) { $o.Invoke() }
  elseif ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$o)) { $o.Toggle() }
}
function Pick($el) { $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function Grab() {
  # The window draws itself into the bitmap (PrintWindow, full content): whatever else is on the screen,
  # a menu or another program over it, stays out of the picture.
  $h = [IntPtr]$main.Current.NativeWindowHandle
  Start-Sleep -Milliseconds 800
  $r = New-Object W4+R; [W4]::GetWindowRect($h, [ref]$r) | Out-Null
  $full = New-Object System.Drawing.Bitmap ($r.Ri - $r.L), ($r.B - $r.T)
  $g = [System.Drawing.Graphics]::FromImage($full)
  $hdc = $g.GetHdc()
  [W4]::PrintWindow($h, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose()
  # Windows 11 keeps a 7 px invisible resize border round the window: leave it out.
  $rect = New-Object System.Drawing.Rectangle 7, 0, ($full.Width - 14), ($full.Height - 7)
  $bmp = $full.Clone($rect, $full.PixelFormat)
  $full.Dispose()
  $bmp
}
function Shot($name) { $b = Grab; $b.Save((Join-Path $outDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); "shot $name" }

# The report page is the white rectangle in the grey preview: crop to it.
function PageShot($name) {
  $b = Grab
  # The page is white paper on grey: its columns are the ones mostly white down the preview, and its rows
  # the longest run that is white just inside the page's left edge (the margin, never drawn on).
  function White($x, $y) { $c = $b.GetPixel($x, $y); $c.R -gt 245 -and $c.G -gt 245 -and $c.B -gt 245 }
  $yTop = 170; $yBot = $b.Height - 36
  $cols = @(); for ($x = 0; $x -lt $b.Width - 20; $x++) {
    $n = 0; $m = 0; for ($y = $yTop; $y -lt $yBot; $y += 15) { $m++; if (White $x $y) { $n++ } }
    if ($n -ge 0.3 * $m) { $cols += $x }
  }
  $l = $cols[0]; $rr = $cols[-1]
  $best = 0; $runStart = -1; $t = 0; $bt = 0
  for ($y = $yTop; $y -le $yBot; $y++) {
    if ($y -lt $yBot -and (White ($l + 4) $y)) { if ($runStart -lt 0) { $runStart = $y } }
    elseif ($runStart -ge 0) { if ($y - $runStart -gt $best) { $best = $y - $runStart; $t = $runStart; $bt = $y - 1 }; $runStart = -1 }
  }
  $rect = New-Object System.Drawing.Rectangle $l, $t, ($rr - $l + 1), ($bt - $t + 1)
  $page = $b.Clone($rect, $b.PixelFormat)
  $page.Save((Join-Path $outDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  "shot $name $($rect.Width)x$($rect.Height)"
  $page.Dispose(); $b.Dispose()
}

$session = "$env:LOCALAPPDATA\SheetNest\session.json"
$sessionBefore = [IO.File]::ReadAllText($session)
[IO.File]::WriteAllText($session, ($sessionBefore -replace '"Language": "[a-z]+"', "`"Language`": `"$language`""))
$exe = Join-Path $PSScriptRoot '..\..\DeepNestSharp\bin\Release\net10.0-windows\SheetNest.exe'
$p = Start-Process $exe -ArgumentList "`"$project`"" -PassThru
try {
Start-Sleep -Seconds 10
$main = $A::RootElement.FindFirst($TS::Children, (Cond $A::ProcessIdProperty $p.Id))
$hwnd = [IntPtr]$main.Current.NativeWindowHandle
[W4]::ShowWindow($hwnd, 9) | Out-Null
[W4]::MoveWindow($hwnd, 40, 20, $width, $height, $true) | Out-Null
Start-Sleep -Seconds 2

Pick (ById 'navNestingBtn'); Start-Sleep -Seconds 2
Press (ById 'btnStartNest')
$deadline = (Get-Date).AddMinutes(4)
while ((Get-Date) -lt $deadline -and (ById 'btnStartNest').Current.IsEnabled -eq $false) { Start-Sleep -Seconds 2 }
Start-Sleep -Seconds 3

# The layout step, where the parts are in their colours.
Pick (ById 'navLayoutBtn'); Start-Sleep -Seconds 3
Shot 'main-nest'
Pick (ById 'navPartsBtn'); Start-Sleep -Seconds 2
Shot 'parts'
Pick (ById 'navLayoutBtn'); Start-Sleep -Seconds 3
$edit = $main.FindAll($TS::Descendants, (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::Button))) | Where-Object { $_.Current.Name -like '*Edit nest*' } | Select-Object -Last 1
if ($edit) { Press $edit; Start-Sleep -Seconds 2; Shot 'edit-nest'; Press $edit; Start-Sleep -Seconds 1 }

Pick (ById 'viewTypeReportRadio'); Start-Sleep -Seconds 4
PageShot 'nest-report'
$next = $main.FindFirst($TS::Descendants, (Cond $A::HelpTextProperty 'Next page'))
if ($next) {
  Press $next; Start-Sleep -Seconds 2
  $scroll = ById 'reportPagesScroll'
  if ($scroll) { $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100); Start-Sleep -Seconds 2 }
  PageShot 'nest-report-layout'
}
}
finally {
  Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
  [IO.File]::WriteAllText($session, $sessionBefore)
}
