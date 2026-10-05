param(
  [switch]$Baseline,
  [string]$Godot = 'C:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
)

$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot)
$previousTiering = $env:DOTNET_TieredCompilation
try {
  $env:DOTNET_TieredCompilation = '0'
  $label = if ($Baseline) { 'baseline' } else { 'final' }
  $defines = if ($Baseline) { 'MAP_STORAGE_BENCHMARK' } else { 'MAP_STORAGE_BENCHMARK%3BCOMPACT_MAP' }
  # Release compilation, with Godot's standard assembly lookup directory as the output.
  dotnet build fun-project.csproj -c Release --no-incremental "-p:DefineConstants=$defines" '-p:OutputPath=.godot/mono/temp/bin/Debug' > "benchmarks/$label-build.log" 2>&1
  if ($LASTEXITCODE -ne 0) { throw "Build failed: benchmarks/$label-build.log" }
  & $Godot --headless --path . --script benchmarks/run-map-storage.gd --log-file "benchmarks/$label-godot.log" > "benchmarks/$label.log" 2>&1
  if ($LASTEXITCODE -ne 0) { throw "Benchmark failed: benchmarks/$label.log" }
  $jsonLines = @(Get-Content "benchmarks/$label.log" | Where-Object { $_.StartsWith('{') })
  if ($jsonLines.Count -ne 3) { throw 'Expected three completed benchmark cases.' }
  $jsonLines | Set-Content "benchmarks/$label.jsonl" -Encoding utf8
  if (!$Baseline -and (Test-Path benchmarks/baseline.jsonl)) {
    $before = @(Get-Content benchmarks/baseline.jsonl | ConvertFrom-Json)
    $after = @($jsonLines | ConvertFrom-Json)
    foreach ($i in 0..2) {
      foreach ($key in @('name', 'dimensions', 'cells', 'checksum', 'spawnChecksum', 'pathChecksum', 'successfulPaths', 'losChecksum', 'clearRays', 'verticalRays')) {
        if ($before[$i].$key -ne $after[$i].$key) { throw "Baseline mismatch: case $i, $key" }
      }
    }
  }
  $jsonLines
} finally {
  $env:DOTNET_TieredCompilation = $previousTiering
  Pop-Location
}
