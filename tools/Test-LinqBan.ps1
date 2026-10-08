# Compile temporary fixtures in the real project, using its analyzer configuration.
# Run with a clean checkout and no concurrent editor/CLI build.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$probeName = 'LinqBanProbe_' + [Guid]::NewGuid().ToString('N')
$sourcePath = Join-Path $projectRoot "$probeName.cs"
$generatedPath = Join-Path $projectRoot "$probeName.g.cs"
$adapterDirectory = Join-Path $projectRoot 'gdunit4_testadapter_v5'
$adapterPath = Join-Path $adapterDirectory "$probeName.cs"
$adapterExisted = Test-Path -LiteralPath $adapterDirectory
$logRoot = Join-Path $projectRoot "TestResults/$probeName"
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

function Build-Probe([string]$configuration, [string]$caseName) {
  $logPath = Join-Path $logRoot "$configuration-$caseName.log"
  & dotnet build fun-project.csproj --configuration $configuration --nologo -v:quiet *> $logPath
  $exitCode = $LASTEXITCODE
  return @{ ExitCode = $exitCode; Text = (Get-Content -LiteralPath $logPath -Raw); Path = $logPath }
}

Push-Location $projectRoot
try {
  foreach ($configuration in @('Debug', 'ExportRelease')) {
    $prohibitedSource = @'
using System.Linq;
using RegularLinq = System.Linq.Enumerable;
namespace FunProject.LinqBanProbe;
internal static class Prohibited {
  internal static object Static(int[] values) => System.Linq.Enumerable.Select(values, value => value * 2); // banned
  internal static object Extension(int[] values) => values.Select(value => value * 2); // banned
  internal static object Query(int[] values) => from value in values where value > 0 select value * 2; // banned
  internal static object Alias(int[] values) => RegularLinq.Where(values, value => value > 0); // banned
  internal static object Queryable(System.Linq.IQueryable<int> values) => values.Where(value => value > 0); // banned
  internal static System.Func<int[], int> MethodGroup() => System.Linq.Enumerable.Count; // banned
}
'@
    Set-Content -LiteralPath $sourcePath -Value $prohibitedSource
    $result = Build-Probe $configuration 'prohibited'
    if ($result.ExitCode -eq 0) { throw "Regular LINQ compiled in $configuration. See $($result.Path)" }
    $sourceLines = $prohibitedSource -split '\r?\n'
    for ($index = 0; $index -lt $sourceLines.Count; $index++) {
      if ($sourceLines[$index] -notmatch '// banned') { continue }
      $location = [regex]::Escape("$probeName.cs($($index + 1),")
      if ($result.Text -notmatch "$location\d+\): error RS0030:") {
        throw "Missing RS0030 for line $($index + 1) in $configuration. See $($result.Path)"
      }
    }
    Write-Host "$configuration rejects static, extension, query, alias, Queryable and method-group LINQ."

    Set-Content -LiteralPath $sourcePath -Value @'
namespace FunProject.LinqBanProbe;
internal static class Allowed {
  internal static int[] ZLinqChain(int[] values) => values.AsValueEnumerable().Where(value => value > 0).Select(value => value * 2).ToArray();
  internal static int[] ZLinqQuery(int[] values) => (from value in values.AsValueEnumerable() where value > 0 select value * 2).ToArray();
  internal static int Unrelated() => Enumerable.Select();
}
internal static class Enumerable {
  internal static int Select() => 42;
}
[System.CodeDom.Compiler.GeneratedCode("LinqBanProbe", "1")]
internal static class AttributedGenerated {
  internal static int Count(int[] values) => System.Linq.Enumerable.Count(values);
}
'@
    $generatedSource = @'
namespace FunProject.LinqBanProbe;
internal static class Generated {
  internal static int Count(int[] values) => System.Linq.Enumerable.Count(values);
}
'@
    Set-Content -LiteralPath $generatedPath -Value $generatedSource
    New-Item -ItemType Directory -Path $adapterDirectory -Force | Out-Null
    Set-Content -LiteralPath $adapterPath -Value ($generatedSource.Replace('class Generated', 'class AdapterGenerated'))
    $result = Build-Probe $configuration 'allowed'
    if ($result.ExitCode -ne 0) { throw "Allowed/generated code failed in $configuration. See $($result.Path)" }
    Write-Host "$configuration accepts ZLinq chains/query syntax, unrelated symbols and generated code."
    Remove-Item -LiteralPath $generatedPath, $adapterPath
  }
}
finally {
  foreach ($path in @($sourcePath, $generatedPath, $adapterPath)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
  }
  if (-not $adapterExisted -and (Test-Path -LiteralPath $adapterDirectory) -and
      @(Get-ChildItem -LiteralPath $adapterDirectory -Force).Count -eq 0) {
    Remove-Item -LiteralPath $adapterDirectory
  }
  Pop-Location
}
Write-Host "LINQ ban verified. Build logs: $logRoot"
