#nullable disable warnings
using System;
using System.Threading.Tasks;
using FunProject.Scenes.Ext;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
// Partial: the suite nests GodotObject-derived test doubles (GD0002).
public partial class PackedSceneExtensionsTest
{
  internal sealed partial class ProbeView : Control;

  [TestCase(TestName = "A typed root instantiates once, live and unparented")]
  public async Task TypedRootIsReturnedLiveAndUnparented()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var view = Pack(new ProbeView()).InstantiateAs<ProbeView>("Test probe");

    Assert.True(view.GetParent() is null); // emitted live and unparented
    AutoFree(view); // register for cleanup or it leaks as an orphan
  }

  [TestCase(TestName = "A wrong root is freed immediately and throws with role and types")]
  public async Task WrongRootIsFreedImmediatelyAndThrows()
  {
    await using var cleanup = new DeferredNodeCleanup();

    // Free (not QueueFree) leaves no orphan behind the synchronous throw.
    long orphansBefore = (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
    InvalidOperationException thrown = null;
    try
    {
      Pack(new Control { Name = "NotAProbeView" }).InstantiateAs<ProbeView>("Test probe");
    }
    catch (InvalidOperationException exception)
    {
      thrown = exception;
    }
    Assert.Equal(orphansBefore,
      (long)Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount));

    Assert.True(thrown is not null, "Expected a wrong root to throw.");
    Assert.True(thrown.Message.Contains("Test probe"), thrown.Message); // the supplied role
    Assert.True(thrown.Message.Contains("ProbeView"), thrown.Message); // expected type
    Assert.True(thrown.Message.Contains("Control"), thrown.Message); // actual root type
  }
}
