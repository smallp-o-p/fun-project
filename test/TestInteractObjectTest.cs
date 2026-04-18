using GdUnit4;
using Godot;

#nullable enable
[TestSuite]
[RequireGodotRuntime]
public partial class TestInteractObjectTest
{
  [TestCase(TestName = "Packed scene instantiates as TestInteractObject")]
  public void PackedSceneInstantiatesAsTestInteractObject()
  {
    PackedScene? packedScene = ResourceLoader.Load<PackedScene>("res://scenes/TestInteractObject.tscn");

    Assert.True(packedScene != null);

    Node? instance = packedScene!.Instantiate();

    try
    {
      Assert.True(instance != null);
      Assert.True(instance is TestInteractObject);
    }
    finally
    {
      instance?.Free();
    }
  }

  [TestCase(TestName = "Test interact object turns its mesh green on click")]
  public void TestInteractObjectTurnsItsMeshGreenOnClick()
  {
    var camera = new Camera3D();
    var interactObject = new TestInteractObject();
    var collisionShape = new CollisionShape3D();
    var meshInstance = new MeshInstance3D();
    var click = CreateMouseButtonEvent(MouseButton.Left, pressed: true);

    try
    {
      collisionShape.AddChild(meshInstance);
      interactObject.AddChild(collisionShape);

      bool handled = interactObject.TryHandleInput(camera, click, Vector3.Zero, Vector3.Up, 0);

      Assert.True(handled);
      Assert.True(meshInstance.MaterialOverride != null);

      StandardMaterial3D? material = meshInstance.MaterialOverride as StandardMaterial3D;
      Assert.True(material != null);
      Assert.True(material!.AlbedoColor.IsEqualApprox(Colors.Green));
    }
    finally
    {
      interactObject.Free();
      camera.Free();
    }
  }

  [TestCase(TestName = "Test interact object ignores clicks when no mesh exists")]
  public void TestInteractObjectIgnoresClicksWhenNoMeshExists()
  {
    var camera = new Camera3D();
    var interactObject = new TestInteractObject();
    var click = CreateMouseButtonEvent(MouseButton.Left, pressed: true);

    try
    {
      bool handled = interactObject.TryHandleInput(camera, click, Vector3.Zero, Vector3.Up, 0);

      Assert.False(handled);
    }
    finally
    {
      interactObject.Free();
      camera.Free();
    }
  }

  private static InputEventMouseButton CreateMouseButtonEvent(MouseButton button, bool pressed)
  {
    return new InputEventMouseButton
    {
      ButtonIndex = button,
      Pressed = pressed
    };
  }
}
