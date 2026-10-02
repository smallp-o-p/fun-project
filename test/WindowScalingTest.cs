using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class WindowScalingTest
{
  [TestCase]
  public async Task RootWindowScalesTheSameHudAtDesktopAspectRatios()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var root = ((SceneTree)Engine.GetMainLoop()).Root;
    Vector2I originalSize = root.Size;
    var hud = AddToTree(CreateHud());
    var button = hud.GetNode<Button>("%UnitsButton");
    try
    {
      Assert.Equal(Window.ContentScaleModeEnum.CanvasItems, root.ContentScaleMode);
      Assert.Equal(Window.ContentScaleAspectEnum.Expand, root.ContentScaleAspect);
      Assert.Equal(new Vector2I(800, 600), root.ContentScaleSize);
      foreach (var size in new[] {
        new Vector2I(800, 600), new Vector2I(1280, 720),
        new Vector2I(1920, 1080), new Vector2I(1920, 1200),
        new Vector2I(2560, 1440), new Vector2I(3440, 1440),
        new Vector2I(3840, 2160), new Vector2I(800, 600),
      })
      {
        root.Size = size;
        await WaitForLayout(root);
        Assert.Equal(size, root.Size);
        float scale = Mathf.Min(size.X / 800f, size.Y / 600f);
        Vector2 logical = root.GetVisibleRect().Size;
        // Godot floors the expanded logical canvas to whole viewport pixels. The
        // resulting physical transform may differ slightly between X and Y.
        Assert.True(logical.DistanceTo((Vector2)size / scale) < 1,
          $"Window {size} should expand to {(Vector2)size / scale}; got {logical}.");
        Assert.True(root.GetFinalTransform().Scale.IsEqualApprox((Vector2)size / logical),
          $"Physical transform must map the entire logical canvas into {size}.");
        Assert.True(new Rect2(Vector2.Zero, logical).Encloses(ScreenRect(button)));
        Assert.True(button.GetThemeFontSize("font_size") * scale >= 16);
        Assert.True(ReferenceEquals(button, hud.GetNode<Button>("%UnitsButton")));
      }
    }
    finally
    {
      root.Size = originalSize;
      await WaitForLayout(root);
    }
  }
}
