using System;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class InteractableTest
{
  [TestCase(TestName = "Interactable subclasses implement IInteractable")]
  public void InteractableSubclassesImplementIInteractable()
  {
    var interactable = new RecordingInteractable();

    try
    {
      Assert.True(interactable is IInteractable);
    }
    finally
    {
      interactable.Free();
    }
  }

  [TestCase(TestName = "Interactable tracks hover state and calls hover hooks")]
  public void InteractableTracksHoverStateAndCallsHooks()
  {
    var interactable = new RecordingInteractable();

    try
    {
      interactable._MouseEnter();

      Assert.True(interactable.IsHovered);
      Assert.True(interactable.HoverEnterCount == 1);

      interactable._MouseExit();

      Assert.False(interactable.IsHovered);
      Assert.True(interactable.HoverExitCount == 1);
    }
    finally
    {
      interactable.Free();
    }
  }

  [TestCase(TestName = "Interactable calls interaction hook for valid click")]
  public void InteractableCallsInteractionHookForValidClick()
  {
    var camera = new Camera3D();
    var interactable = new RecordingInteractable();
    var click = CreateMouseButtonEvent(MouseButton.Left, pressed: true);

    try
    {
      bool handled = interactable.TryHandleInput(camera, click, Vector3.Zero, Vector3.Up, 2);

      Assert.True(handled);
      Assert.True(interactable.InteractionCount == 1);
      Assert.True(interactable.LastShapeIndex == 2);
    }
    finally
    {
      interactable.Free();
      camera.Free();
    }
  }

  [TestCase(TestName = "Interactable ignores click when CanInteract rejects it")]
  public void InteractableIgnoresClickWhenCanInteractRejectsIt()
  {
    var camera = new Camera3D();
    var interactable = new RecordingInteractable
    {
      AllowInteraction = false
    };
    var click = CreateMouseButtonEvent(MouseButton.Left, pressed: true);

    try
    {
      bool handled = interactable.TryHandleInput(camera, click, Vector3.Zero, Vector3.Up, 0);

      Assert.False(handled);
      Assert.True(interactable.InteractionCount == 0);
    }
    finally
    {
      interactable.Free();
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

  private sealed partial class RecordingInteractable : Interactable
  {
    public bool AllowInteraction { get; set; } = true;
    public int HoverEnterCount { get; private set; }
    public int HoverExitCount { get; private set; }
    public int InteractionCount { get; private set; }
    public int LastShapeIndex { get; private set; } = -1;

    public override bool CanInteract(Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIdx)
    {
      ArgumentNullException.ThrowIfNull(camera);

      return AllowInteraction;
    }

    public override void OnInteracted(Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIdx)
    {
      ArgumentNullException.ThrowIfNull(camera);

      InteractionCount++;
      LastShapeIndex = shapeIdx;
    }

    public override void OnHoverStarted()
    {
      HoverEnterCount++;
    }

    public override void OnHoverEnded()
    {
      HoverExitCount++;
    }
  }
}
