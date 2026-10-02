#nullable disable warnings
using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;
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
    Assert.False(query.CollideWithAreas);
    Assert.True(query.CollideWithBodies);
  }

  [TestCase(TestName = "Viewport ray query includes only terrain and ignores prop collision layers")]
  public void ViewportRayQueryIncludesOnlyTerrainAndIgnoresPropCollisionLayers()
  {
    var camera = AutoFree(new Camera3D());

    PhysicsRayQueryParameters3D query = GameCamera.CreateViewportRayQuery(camera, Vector2.Zero);

    Assert.Equal(1u, query.CollisionMask);
    Assert.Equal(0u, query.CollisionMask & 2u);
  }

  [TestCase(TestName = "Viewport raycast selects ground beneath a prop roof")]
  public async Task ViewportRaycastSelectsGroundBeneathAPropRoof()
  {
    await using var cleanup = new DeferredNodeCleanup();
    var viewport = AddToTree(new SubViewport { OwnWorld3D = true, Size = new Vector2I(320, 180) });
    var camera = new Camera3D
    {
      Position = new Vector3(0.0f, 10.0f, 0.0f),
      RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
      Current = true,
    };
    viewport.AddChild(camera);

    var ground = new StaticBody3D { CollisionLayer = 1u, Position = new Vector3(0.0f, -0.5f, 0.0f) };
    ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(10.0f, 1.0f, 10.0f) } });
    viewport.AddChild(ground);
    var roof = new StaticBody3D { CollisionLayer = 2u, Position = new Vector3(0.0f, 3.0f, 0.0f) };
    roof.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(10.0f, 1.0f, 10.0f) } });
    viewport.AddChild(roof);

    // Wait for the new bodies to enter the physics world before querying it.
    for (int i = 0; i < 3; i++)
      await viewport.ToSignal(viewport.GetTree(), SceneTree.SignalName.PhysicsFrame);

    Vector2 viewportPosition = new(160.0f, 90.0f);
    using PhysicsRayQueryParameters3D unfilteredQuery =
      PhysicsRayQueryParameters3D.Create(camera.GlobalPosition, new Vector3(0.0f, -10.0f, 0.0f));
    Godot.Collections.Dictionary unfilteredHit = camera.GetWorld3D().DirectSpaceState.IntersectRay(unfilteredQuery);
    Assert.True(ReferenceEquals(roof, unfilteredHit["collider"].AsGodotObject()));

    Vector3 groundHit = GameCamera.TryRaycastViewportPosition(camera, camera.GetWorld3D(), viewportPosition).RequireSome();

    Assert.True(groundHit.IsEqualApprox(Vector3.Zero));
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
