using Godot;

// Fixed, HUD-aware initial framing. Native canvas scaling handles window resizing;
// there is no player pan/zoom input or automatic refit after setup.
public sealed partial class GeoscapeCameraRig : Camera2D
{
  public void Setup(Vector2I mapSize, Rect2 availableArea)
  {
    // The smaller ratio is the largest uniform zoom that still shows the whole map.
    float fit = float.Min(availableArea.Size.X / mapSize.X, availableArea.Size.Y / mapSize.Y);
    Vector2 viewportCenter = GetViewport().GetVisibleRect().GetCenter();
    Zoom = new Vector2(fit, fit);
    Position = (Vector2)mapSize / 2f + (viewportCenter - availableArea.GetCenter()) / fit;
    MakeCurrent();
    ForceUpdateScroll();
  }
}
