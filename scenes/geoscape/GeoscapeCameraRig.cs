using Godot;

// Fits the authored map to the unobscured area supplied by the scene. Layout changes
// may refit the default framing; explicit camera position/zoom changes take ownership
// and are left untouched. There is no per-frame reset or player pan/zoom input here.
public sealed partial class GeoscapeCameraRig : Camera2D
{
  private Vector2I _mapSize;
  private Vector2 _framedPosition;
  private Vector2 _framedZoom;
  private bool _hasAutomaticFrame;

  public void Setup(Vector2I mapSize)
  {
    _mapSize = mapSize;
    _hasAutomaticFrame = false;
    MakeCurrent();
    FitToArea(GetViewport().GetVisibleRect());
  }

  public void FitToArea(Rect2 availableArea)
  {
    if (_hasAutomaticFrame
      && (!Position.IsEqualApprox(_framedPosition) || !Zoom.IsEqualApprox(_framedZoom)))
      return;
    if (availableArea.Size.X <= 0 || availableArea.Size.Y <= 0)
      return;

    // The smaller ratio is the largest uniform zoom that still shows the whole map.
    float fit = float.Min(availableArea.Size.X / _mapSize.X, availableArea.Size.Y / _mapSize.Y);
    Vector2 viewportCenter = GetViewport().GetVisibleRect().GetCenter();
    Zoom = new Vector2(fit, fit);
    Position = (Vector2)_mapSize / 2f + (viewportCenter - availableArea.GetCenter()) / fit;
    _framedPosition = Position;
    _framedZoom = Zoom;
    _hasAutomaticFrame = true;
    ForceUpdateScroll(); // also update while the retained map view is covered/disabled
  }
}
