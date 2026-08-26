using Godot;

// Static framing for the geoscape first pass: fit the authored map to the viewport once and
// center on it. No interaction — zoom and pan were removed to keep the first pass simple;
// when they return, they grow back here.
public sealed partial class GeoscapeCameraRig : Camera2D
{
  public void Setup(Vector2I mapSize)
  {
    // Zoom values > 1 magnify; fit-map zoom = the smaller ratio, i.e. the largest zoom where
    // the viewport still covers the whole map (the larger ratio would crop the other axis).
    Vector2 viewport = GetViewport().GetVisibleRect().Size;
    float fit = float.Min(viewport.X / mapSize.X, viewport.Y / mapSize.Y);
    Zoom = new Vector2(fit, fit);
    Position = new Vector2(mapSize.X / 2f, mapSize.Y / 2f);
    MakeCurrent();
  }
}
