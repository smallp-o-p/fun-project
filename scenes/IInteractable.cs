using Godot;

public interface IInteractable
{
  public bool CanInteract(Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIdx);
  public void OnInteracted(Camera3D camera, Vector3 eventPosition, Vector3 normal, int shapeIdx);
  public void OnHoverStarted();
  public void OnHoverEnded();
}
