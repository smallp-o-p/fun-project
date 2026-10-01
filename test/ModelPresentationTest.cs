using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public class ModelPresentationTest
{
  private static SubViewportContainer CreatePresentation()
    => GD.Load<PackedScene>("res://scenes/ui/ModelPresentation.tscn")
      .Instantiate<SubViewportContainer>();

  [TestCase]
  public async Task EmptyPresentationHasItsOwnTransparentWorldAndNoModel()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var view = AddToTree(CreatePresentation());
    var viewport = view.GetNode<SubViewport>("%ModelViewport");

    Assert.True(view.Stretch);
    Assert.True(viewport.OwnWorld3D);
    Assert.True(viewport.TransparentBg);
    Assert.False(ReferenceEquals(viewport.FindWorld3D(), view.GetViewport().FindWorld3D()));
    Assert.Equal(0, view.GetNode<Node3D>("%ModelRoot").GetChildCount());
  }

  [TestCase]
  public async Task RebindingSameSceneKeepsInstanceAndClearingReleasesIt()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var model = Pack(new MeshInstance3D { Mesh = new BoxMesh(), Name = "Model" });
    var view = AddToTree(CreatePresentation());
    view.Call("PresentModel", model);
    var root = view.GetNode<Node3D>("%ModelRoot");
    Assert.Equal(1, root.GetChildCount());
    var first = root.GetChild(0);

    view.Call("PresentModel", model);
    Assert.True(ReferenceEquals(first, root.GetChild(0)));
    view.Call("PresentModel", default(Variant));

    Assert.Equal(0, root.GetChildCount());
    Assert.False(GodotObject.IsInstanceValid(first));
  }

  [TestCase]
  public async Task VerticalCompositionCanCropWideLimbsWithoutZoomingOut()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var model = Pack(new MeshInstance3D
    {
      Mesh = new BoxMesh { Size = new Vector3(2, 2, 1) },
      Position = new Vector3(0, 1, 0),
    });
    var view = CreatePresentation();
    view.Set("FitWidth", false);
    CreateUiViewport(view, new Vector2I(800, 600));
    view.Size = new Vector2(200, 400);
    view.Call("PresentModel", model);
    await WaitForLayout(view);

    Assert.Equal(1.6f, view.GetNode<Camera3D>("%ModelCamera").Size);
  }

  [TestCase]
  public async Task FramingTracksContainerResizeWithoutCapturingInput()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var model = Pack(new MeshInstance3D
    {
      Mesh = new BoxMesh { Size = new Vector3(1, 2, 1) },
      Position = new Vector3(0, 1, 0),
    });
    var view = CreatePresentation();
    CreateUiViewport(view, new Vector2I(800, 600));
    view.Size = new Vector2(400, 400);
    view.Call("PresentModel", model);
    await WaitForLayout(view);
    var camera = view.GetNode<Camera3D>("%ModelCamera");
    float wideSize = camera.Size;
    view.Size = new Vector2(200, 400);
    await WaitForLayout(view);

    Assert.True(camera.Size >= wideSize);
    Assert.Equal(Control.MouseFilterEnum.Ignore, view.MouseFilter);
    var instance = view.GetNode<Node3D>("%ModelRoot").GetChild(0);
    Assert.False(instance.IsProcessingInput());
    Assert.False(instance.IsProcessingUnhandledInput());
  }
}
