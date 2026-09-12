#nullable disable warnings
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class MovementLineTest
{
  [TestCase(TestName = "Runtime nodes start hidden when entering the tree")]
  public void RuntimeNodesStartHiddenWhenEnteringTheTree()
  {
    MovementLine line = AutoFree(new MovementLine());
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(line);

    Assert.False(line.Visible);
  }

  [TestCase(TestName = "ShowPath shows a valid path and hides an invalid one")]
  public void ShowPathTogglesVisibilityWithMeshContent()
  {
    MovementLine line = AutoFree(new MovementLine());
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(line);

    line.ShowPath([new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 1.0f)]);
    Assert.True(line.Visible);
    Assert.Equal(1, line.Mesh.GetSurfaceCount());

    line.ShowPath([new Vector3(0.0f, 0.0f, 0.0f)]);
    Assert.False(line.Visible);
    Assert.Equal(0, line.Mesh.GetSurfaceCount());
  }

  [TestCase(TestName = "ShowPath applies the line color as the override material albedo")]
  public void ShowPathAppliesLineColorToTheOverrideMaterial()
  {
    MovementLine line = AutoFree(new MovementLine());
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(line);
    line.LineColor = Colors.Red;

    line.ShowPath([new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f)]);

    StandardMaterial3D material = line.MaterialOverride as StandardMaterial3D;
    Assert.True(material != null);
    Assert.True(material.AlbedoColor.IsEqualApprox(Colors.Red));
  }
}
