<#
.SYNOPSIS
  Прогоняет движок по всем заданиям из jobs\, проверяет ответы и сравнивает с эталоном.

.EXAMPLE
  .\run-all.ps1 -Engine .\bin\sparrow.exe
  .\run-all.ps1 -Engine C:\work\my-engine\target\release\my-engine.exe -Seeds 1,2,3 -Iterations 300
  .\run-all.ps1 -Engine .\bin\sparrow.exe -Jobs stoika -Seeds 1..5

  Для каждого задания и зерна создаётся пустая папка runs\<имя>\<job>-s<seed>\, движок запускается в ней
  так же, как его запускает SheetNest, затем tools\check.mjs проверяет output\final_job.json и рисует
  layout.svg. В конце таблица и runs\<имя>\summary.csv. Ширина сравнивается с лучшей у эталона
  (reference\sparrow\summary.csv): отрицательный Δ — короче эталона, то есть лучше.
#>
param(
  [Parameter(Mandatory = $true)] [string] $Engine,
  [int] $Iterations = 300,
  [int[]] $Seeds = @(1),
  [string[]] $Jobs = @(),
  [string] $Name = '',
  [string] $Reference = 'sparrow',
  [int] $TimeoutSec = 300,
  [string] $ExtraArgs = '',
  [string] $Set = 'jobs'
)

$ErrorActionPreference = 'Stop'
$kit = $PSScriptRoot
$Engine = (Resolve-Path $Engine).Path
if (-not $Name) { $Name = [IO.Path]::GetFileNameWithoutExtension($Engine) }
$out = Join-Path $kit "runs\$Name"
New-Item -ItemType Directory -Force $out | Out-Null

$jobFiles = Get-ChildItem (Join-Path $kit $Set) -Filter *.json | Sort-Object Name
if ($Jobs.Count) { $jobFiles = $jobFiles | Where-Object { $Jobs -contains $_.BaseName } }

function Read-Best($engine) {
  $best = @{}
  $csv = Join-Path $kit "reference\$engine\summary.csv"
  if (($Name -ne $engine) -and (Test-Path $csv)) {
    foreach ($r in Import-Csv $csv) {
      if ($r.ok -eq 'True') {
        $w = [double]::Parse($r.width, [Globalization.CultureInfo]::InvariantCulture)
        if (-not $best.ContainsKey($r.job) -or $w -lt $best[$r.job]) { $best[$r.job] = $w }
      }
    }
  }
  return $best
}
$ref = Read-Best $Reference
$rnest = Read-Best 'rnest'
$sameAsRNest = 0

$inv = [Globalization.CultureInfo]::InvariantCulture
$rows = @()
foreach ($job in $jobFiles) {
  foreach ($seed in $Seeds) {
    $dir = Join-Path $out "$($job.BaseName)-s$seed"
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory $dir | Out-Null

    $argLine = "-i `"$($job.FullName)`" --max-iterations $Iterations -x -s $seed --workers 1 $ExtraArgs".Trim()
    $psi = New-Object Diagnostics.ProcessStartInfo $Engine, $argLine
    $psi.WorkingDirectory = $dir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEndAsync()
    $stderr = $p.StandardError.ReadToEndAsync()
    $timedOut = -not $p.WaitForExit($TimeoutSec * 1000)
    if ($timedOut) { $p.Kill($true); $p.WaitForExit() }
    $clock.Stop()
    Set-Content -Path (Join-Path $dir 'log.txt') -Value ($stdout.Result + $stderr.Result) -Encoding utf8

    $final = Join-Path $dir 'output\final_job.json'
    $row = [ordered]@{ job = $job.BaseName; seed = $seed; ok = $false; placed = ''; width = ''; density = ''; overlaps = ''; seconds = $clock.Elapsed.TotalSeconds.ToString('F2', $inv); delta = ''; vs_rnest = ''; note = '' }
    if ($timedOut) {
      $row.note = "timeout $TimeoutSec s"
    } elseif (-not (Test-Path $final)) {
      $row.note = "no output\final_job.json (exit $($p.ExitCode))"
    } else {
      $json = & node (Join-Path $kit 'tools\check.mjs') $job.FullName $final --svg (Join-Path $dir 'layout.svg') --json
      $r = $json | ConvertFrom-Json
      $row.ok = [bool]$r.ok
      $row.placed = "$($r.placed)/$($r.demand)"
      $row.width = ([double]$r.strip_width).ToString('F1', $inv)
      $row.density = ([double]$r.density * 100).ToString('F2', $inv)
      $row.overlaps = $r.overlaps
      if ($ref.ContainsKey($job.BaseName)) {
        $row.delta = ((([double]$r.strip_width) / $ref[$job.BaseName] - 1) * 100).ToString('+0.0;-0.0;0.0', $inv) + '%'
      }
      if ($rnest.ContainsKey($job.BaseName)) {
        $row.vs_rnest = ((([double]$r.strip_width) / $rnest[$job.BaseName] - 1) * 100).ToString('+0.0;-0.0;0.0', $inv) + '%'
        if ([math]::Abs([double]$r.strip_width - $rnest[$job.BaseName]) -lt 0.05) { $sameAsRNest++ }
      }
      $row.note = ($r.errors -join '; ')
    }
    $rows += [pscustomobject]$row
  }
}

$rows | Export-Csv -NoTypeInformation -Encoding utf8 (Join-Path $out 'summary.csv')
$rows | Format-Table job, seed, ok, placed, width, density, overlaps, seconds, delta, vs_rnest, note -AutoSize | Out-String -Width 220
Write-Host "delta — к лучшей ширине Sparrow, vs_rnest — к лучшей ширине RNest; минус значит короче, то есть лучше."
if ($rows.Count -and $sameAsRNest -eq $rows.Count) {
  Write-Warning "Все ширины совпадают с RNest до 0.05 мм: это копия RNest, а не улучшение (см. TASK.md, «Главное»)."
}
Write-Host "Ответы и картинки: $out"
if ($rows | Where-Object { -not $_.ok }) { exit 1 }
