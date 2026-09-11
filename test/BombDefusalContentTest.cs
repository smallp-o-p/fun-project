using System;
using System.Diagnostics;
using System.Threading.Tasks;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class BombDefusalContentTest
{
  [TestCase(TestName = "Authored bomb defusal content starts and expires at the deadline")]
  public void AuthoredContentPlays()
  {
    string godotBin = System.Environment.GetEnvironmentVariable("GODOT_BIN")
      ?? throw new InvalidOperationException("GODOT_BIN was not provided to the test process.");
    // The suite runs inside the main project root now, so the probe targets this same
    // project; it still needs its own headless process because it installs a SceneTree
    // main loop that cannot run inside the test runtime's SceneTree.
    string mainRoot = ProjectSettings.GlobalizePath("res://");

    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
      FileName = godotBin,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    process.StartInfo.ArgumentList.Add("--headless");
    process.StartInfo.ArgumentList.Add("--path");
    process.StartInfo.ArgumentList.Add(mainRoot);
    process.StartInfo.ArgumentList.Add("--script");
    process.StartInfo.ArgumentList.Add("res://tests/BattleTypeContentProbe.cs");
    process.StartInfo.ArgumentList.Add("--quit-after");
    process.StartInfo.ArgumentList.Add("1000");
    process.StartInfo.ArgumentList.Add("--verbose");

    process.Start();
    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
    Task<string> stderrTask = process.StandardError.ReadToEndAsync();
    bool exited = process.WaitForExit(120000);
    if (!exited)
      process.Kill(entireProcessTree: true);
    Task.WaitAll(stdoutTask, stderrTask);

    string stdout = stdoutTask.Result;
    string stderr = stderrTask.Result;
    string output = $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}";
    Assert.True(exited, $"Bomb defusal probe timed out.\n{output}");
    Assert.Equal(0, process.ExitCode, output);
    Assert.True(stdout.Contains("BOMB_DEFUSAL_CONTENT_OK"), output);
    Assert.True(stdout.Contains("SKIRMISH_CONTENT_OK"), output);
  }
}
