param(
  [Parameter(Mandatory)][string]$Name,
  [string]$Filter = '',
  [string]$OutputRoot = 'TestResults/coverage'
)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Use a simple run name.' }
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
  $output = Join-Path $OutputRoot $Name
  if (Test-Path $output) { throw "Run directory already exists: $output" }
  New-Item -ItemType Directory -Path $output -Force | Out-Null
  $binary = '.godot/mono/temp/bin/Debug/fun-project.dll'
  $symbols = '.godot/mono/temp/bin/Debug/fun-project.pdb'
  $before = @(Get-FileHash $binary, $symbols | Select-Object Path, Hash)
  $testArgs = @('test', 'fun-project.csproj', '--no-build', '--no-restore', '--results-directory', $output)
  if ($Filter) { $testArgs += @('--filter', $Filter) }
  $existingGodot = @(Get-CimInstance Win32_Process | Where-Object Name -Like '*Godot*' | ForEach-Object ProcessId)
  $timer = [Diagnostics.Stopwatch]::StartNew()
  & dotnet dotnet-coverage collect --settings .config/coverage.xml --include-files $binary `
    --output "$output/coverage.cobertura.xml" --output-format cobertura dotnet @testArgs *> "$output/run.log"
  $exitCode = $LASTEXITCODE
  $timer.Stop()
  $after = @(Get-FileHash $binary, $symbols | Select-Object Path, Hash)
  if (Compare-Object $before $after -Property Path, Hash) { throw 'Collector did not restore the original DLL/PDB.' }
  [xml]$trx = Get-Content "$output/test-result.trx"
  $counts = $trx.TestRun.ResultSummary.Counters
  [xml]$coverage = Get-Content "$output/coverage.cobertura.xml"
  $classes = @($coverage.coverage.packages.package.classes.class)
  $excludedSourcePattern = '[\\/](test|gdunit4_testadapter_v5|\.godot|obj)[\\/]'
  if (@($classes | Where-Object filename -Match $excludedSourcePattern).Count) {
    throw 'Test or generated source leaked into coverage.'
  }
  if ([int]$coverage.coverage.'lines-covered' -eq 0) { throw 'No production coverage collected.' }
  $children = @(Get-CimInstance Win32_Process | Where-Object {
      $_.Name -like '*Godot*' -and $_.ProcessId -notin $existingGodot
    } | Select-Object ProcessId, CommandLine)
  $summary = [ordered]@{
    revision = (& git rev-parse HEAD)
    workingTreeStatus = @(& git status --porcelain)
    filter = $Filter
    command = @('dotnet', 'dotnet-coverage', 'collect', '--settings', '.config/coverage.xml', '--include-files', $binary,
      '--output', "$output/coverage.cobertura.xml", '--output-format', 'cobertura', 'dotnet') + $testArgs
    exitCode = $exitCode
    wallSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
    testCounts = [ordered]@{ total = [int]$counts.total; passed = [int]$counts.passed; failed = [int]$counts.failed; skipped = [int]$counts.notExecuted }
    binariesRestored = $true
    residualGodotProcesses = $children
    coverage = [ordered]@{
      linesCovered = [int]$coverage.coverage.'lines-covered'
      linesValid = [int]$coverage.coverage.'lines-valid'
      branchesCovered = [int]$coverage.coverage.'branches-covered'
      branchesValid = [int]$coverage.coverage.'branches-valid'
    }
  }
  $summary | ConvertTo-Json -Depth 5 | Set-Content "$output/run-summary.json"
  if ($exitCode -ne 0 -or [int]$counts.total -eq 0 -or [int]$counts.failed -gt 0 -or $children.Count) {
    Get-Content "$output/run.log" -Tail 40
    throw 'Coverage run failed, found no tests, or left Godot running; inspect artifacts.'
  }
  & dotnet reportgenerator "-reports:$output/coverage.cobertura.xml" "-targetdir:$output/html" `
    '-reporttypes:Html;TextSummary;JsonSummary' *> "$output/report.log"
  if ($LASTEXITCODE -ne 0) { throw 'Report generation failed.' }
  $summary | ConvertTo-Json -Depth 5
}
finally { Pop-Location }
