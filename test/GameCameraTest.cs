#nullable disable warnings
using System;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class GameCameraTest
{
  [TestCase(TestName = "Viewport ray query uses the camera projection and requested length")]
  public void ViewportRayQueryUsesTheCameraProjectionAndRequestedLength()
  {
    var camera = AutoFree(new Camera3D
    {
      Projection = Camera3D.ProjectionType.Perspective,
      Fov = 70.0f,
      GlobalPosition = new Vector3(1.0f, 2.0f, 3.0f)
    });
    Vector2 viewportPosition = new(320.0f, 180.0f);
    float rayLength = 75.0f;

    PhysicsRayQueryParameters3D query =
      GameCamera.CreateViewportRayQuery(camera, viewportPosition, rayLength);
    Vector3 expectedOrigin = camera.ProjectRayOrigin(viewportPosition);
    Vector3 expectedEnd = expectedOrigin + (camera.ProjectRayNormal(viewportPosition) * rayLength);

    Assert.True(query.From.IsEqualApprox(expectedOrigin));
    Assert.True(query.To.IsEqualApprox(expectedEnd));
    Assert.Equal(1u, query.CollisionMask);
    Assert.False(query.CollideWithAreas);
    Assert.True(query.CollideWithBodies);
  }

  [TestCase(TestName = "Viewport ray query throws when camera is missing")]
  public void ViewportRayQueryThrowsWhenCameraIsMissing()
  {
    Assert.Throws<ArgumentNullException>(() => GameCamera.CreateViewportRayQuery(null, Vector2.Zero));
  }

  [TestCase(TestName = "Viewport ray query throws when ray length is invalid")]
  public void ViewportRayQueryThrowsWhenRayLengthIsInvalid()
  {
    var camera = AutoFree(new Camera3D());

    Assert.Throws<ArgumentOutOfRangeException>(() => GameCamera.CreateViewportRayQuery(camera, Vector2.Zero, 0.0f));
  }

  [TestCase(TestName = "Viewport raycast throws when world is missing")]
  public void ViewportRaycastThrowsWhenWorldIsMissing()
  {
    var camera = AutoFree(new Camera3D());

    Assert.Throws<ArgumentNullException>(() => GameCamera.TryRaycastViewportPosition(camera, null, Vector2.Zero));
  }
}
