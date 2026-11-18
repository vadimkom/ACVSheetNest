<#
.SYNOPSIS
  Сводит работы всех агентов (ветки agent/*) в одну таблицу, перемеряя их на этой машине.

.EXAMPLE
  .\compare-agents.ps1
  .\compare-agents.ps1 -Agents iceman,claude-opus -Seeds 1,2,3
  .\compare-agents.ps1 -UseTags                # брать последний тег <имя>-vN, а не голову ветки
  .\compare-agents.ps1 -RerunReference         # перемерить и Sparrow с RNest в этом же прогоне

  Для каждой ветки origin/agent/<имя> берётся dist\*.exe (через git archive, рабочая копия не меняется),
  прогоняется run-all.ps1 на одних и тех же заданиях и зёрнах, по очереди, чтобы время было сравнимо.
  Итог: таблица на экране, runs\compare\summary.csv и runs\compare\summary.md.
  Агенты на машине в это время лучше не работают: они отнимают процессор и портят время.
#>
param(
  [string[]] $Agents = @(),
  [int[]] $Seeds = @(1, 2, 3),
  [int] $Iterations = 300,
  [string[]] $Jobs = @(),
  [switch] $UseTags,
  [switch] $RerunReference,
  [switch] $NoFetch,
  [string] $Set = 'jobs'
)

$ErrorActionPreference = 'Stop'
$kit = $PSScriptRoot
$inv = [Globalization.CultureInfo]::InvariantCulture
$outName = if ($Set -eq 'jobs') { 'compare' } else { "compare-$Set" }
$out = Join-Path $kit "runs\$outName"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

Push-Location $kit
try {
  if (-not $NoFetch) { git fetch --quiet --prune --tags origin 2>&1 | Out-Null }

  $branches = git for-each-ref --format='%(refname:short)' 'refs/remotes/origin/agent/' | Where-Object { $_ }
  $entries = @()
  foreach ($b in $branches) {
    $name = $b -replace '^origin/agent/', ''
    if ($Agents.Count -and $Agents -notcontains $name) { continue }
    $ref = $b
    if ($UseTags) {
      $tag = git tag --list "$name-v*" --sort=-v:refname | Select-Object -First 1
      if ($tag) { $ref = $tag }
    }
    $entries += [pscustomobject]@{ Name = $name; Ref = $ref; Commit = (git rev-parse --short "$ref^{commit}") }
  }
  if (-not $entries.Count) { Write-Warning 'Нет веток origin/agent/*'; return }

  # эталоны: либо перемерить, либо взять из reference\
  $engines = @()
  if ($RerunReference) {
    $engines += [pscustomobject]@{ Name = 'sparrow'; Exe = (Join-Path $kit 'bin\sparrow.exe'); Ref = 'bin'; Commit = '' }
    $engines += [pscustomobject]@{ Name = 'rnest'; Exe = (Join-Path $kit 'bin\rnest-sparrow.exe'); Ref = 'bin'; Commit = '' }
  }

  foreach ($e in $entries) {
    $dir = Join-Path $out "exe\$($e.Name)"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $dir 'dist.zip'
    git archive --format=zip -o $zip $e.Ref dist 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $zip)) {
      Write-Warning "$($e.Name): в $($e.Ref) нет папки dist"
      $engines += [pscustomobject]@{ Name = $e.Name; Exe = $null; Ref = $e.Ref; Commit = $e.Commit }
      continue
    }
    Expand-Archive -Force $zip $dir
    $exe = Get-ChildItem (Join-Path $dir 'dist') -Filter *.exe | Sort-Object { $_.BaseName -ne $e.Name } | Select-Object -First 1
    if (-not $exe) { Write-Warning "$($e.Name): в dist нет .exe" }
    $engines += [pscustomobject]@{ Name = $e.Name; Exe = $exe.FullName; Ref = $e.Ref; Commit = $e.Commit }
  }

  # прогоны, строго по очереди
  $results = @{}
  foreach ($e in $engines) {
    if (-not $e.Exe) { continue }
    Write-Host "== $($e.Name) ($($e.Ref) $($e.Commit))"
    $runName = "$outName\run-$($e.Name)"
    & (Join-Path $kit 'run-all.ps1') -Engine $e.Exe -Name $runName -Seeds $Seeds -Iterations $Iterations -Jobs $Jobs -Set $Set | Out-Null
    $csv = Join-Path $kit "runs\$runName\summary.csv"
    if (Test-Path $csv) { $results[$e.Name] = @(Import-Csv $csv) }
  }
  if (-not $RerunReference) {
    foreach ($r in 'sparrow', 'rnest') {
      $results[$r] = @(Import-Csv (Join-Path $kit "reference\$r\summary.csv"))
      $engines = @([pscustomobject]@{ Name = $r; Exe = 'reference'; Ref = 'reference'; Commit = '' }) + $engines
    }
  }

  # лучшая ширина RNest по заданию — для vs_rnest и минимума (−3 %)
  $best = {
    param($rows, $job)
    $ok = $rows | Where-Object { $_.job -eq $job -and $_.ok -eq 'True' }
    if (-not $ok) { return $null }
    ($ok | ForEach-Object { [double]::Parse($_.width, $inv) } | Measure-Object -Minimum).Minimum
  }
  $jobNames = if ($Jobs.Count) { $Jobs } else { Get-ChildItem (Join-Path $kit $Set) -Filter *.json | Sort-Object Name | ForEach-Object BaseName }
  $rnestBest = @{}
  foreach ($j in $jobNames) { $rnestBest[$j] = & $best $results['rnest'] $j }
  $minimumJobs = @('porta', 'stoika', 'stoika-free')

  $table = @()
  foreach ($e in $engines) {
    $rows = @($results[$e.Name] | Where-Object { $_ -and $jobNames -contains $_.job })
    $line =[ordered]@{ agent = $e.Name; ref = (($e.Ref -replace '^origin/', '') + ' ' + $e.Commit).Trim() }
    if (-not $rows) {
      $line.all_ok = 'нет exe'
      $table += [pscustomobject]$line
      continue
    }
    $minimum = $true
    $same = $true
    foreach ($j in $jobNames) {
      $w = & $best $rows $j
      $r = $rnestBest[$j]
      if ($null -eq $w) {
        $line[$j] = if ($rows | Where-Object { $_.job -eq $j }) { 'FAIL' } else { '—' }
        $minimum = $false
        $same = $false
        continue
      }
      $vs = if ($r) { ($w / $r - 1) * 100 } else { 0 }
      $line[$j] = $w.ToString('F1', $inv) + ' (' + $vs.ToString('+0.0;-0.0;0.0', $inv) + '%)'
      if ($r -and [math]::Abs($w - $r) -ge 0.05) { $same = $false }
      if ($minimumJobs -contains $j -and $vs -gt -3) { $minimum = $false }
      if ($j -eq 'test1' -and $r -and $w -gt $r + 1) { $minimum = $false }
    }
    $secs = $rows | ForEach-Object { [double]::Parse($_.seconds, $inv) }
    $line.max_s = ($secs | Measure-Object -Maximum).Maximum.ToString('F1', $inv)
    $line.sum_s = ($secs | Measure-Object -Sum).Sum.ToString('F1', $inv)
    $line.all_ok = -not ($rows | Where-Object { $_.ok -ne 'True' })
    if ($Set -ne 'jobs') { $minimum = ($secs | Measure-Object -Maximum).Maximum -le 60 }   # доп. проверка: всё верно и не дольше 60 с
    $line.minimum = if ($e.Name -in 'sparrow', 'rnest') { '' } elseif ($minimum -and $line.all_ok) { 'да' } else { 'нет' }
    $line.copy_of_rnest = if ($e.Name -ne 'rnest' -and $same) { 'ДА' } else { '' }
    $table += [pscustomobject]$line
  }

  $table | Export-Csv -NoTypeInformation -Encoding utf8 (Join-Path $out 'summary.csv')
  $cols = @('agent', 'ref') + $jobNames + @('max_s', 'sum_s', 'all_ok', 'minimum', 'copy_of_rnest')
  $md = @()
  $md += '| ' + ($cols -join ' | ') + ' |'
  $md += '|' + ('---|' * $cols.Count)
  foreach ($t in $table) { $md += '| ' + (($cols | ForEach-Object { "$($t.$_)" }) -join ' | ') + ' |' }
  $md += ''
  $md += "Зёрна $($Seeds -join ','), --max-iterations $Iterations, $(Get-Date -Format 'yyyy-MM-dd HH:mm'), $env:COMPUTERNAME."
  $md += 'Ширина — лучшая из зёрен, в скобках к лучшей у RNest (минус — короче). Минимум: porta, stoika, stoika-free на 3 % короче RNest, test1 не хуже.'
  if (-not $RerunReference) { $md += 'Sparrow и RNest взяты из reference\ (их время мерено в другой раз).' }
  Set-Content -Encoding utf8 (Join-Path $out 'summary.md') $md

  $table | Format-Table ($cols) -AutoSize | Out-String -Width 250
  Write-Host 'Ширина — лучшая из зёрен, в скобках к лучшей у RNest (минус — короче, то есть лучше).'
  Write-Host "Таблица: $out\summary.md; прогоны каждого: $out\run-<имя>\"
}
finally {
  Pop-Location
}
