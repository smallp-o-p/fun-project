using System;
using System.Threading.Tasks;
using Godot;

namespace FunProject.Tests;

// Drain queued nodes before GdUnit closes the test case's orphan-monitoring window.
// Awaited disposal also runs when an assertion throws; AfterTest runs too late in GdUnit 5.
internal sealed class DeferredNodeCleanup : IAsyncDisposable
{
  public async ValueTask DisposeAsync()
    => await GeoscapeTestScenes.WaitForDeferredDeletion((SceneTree)Engine.GetMainLoop());
}
