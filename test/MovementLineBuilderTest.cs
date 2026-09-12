#nullable disable warnings
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class MovementLineBuilderTest
{
  private const int RadialSegments = 64;
  private const int RingVertexCount = RadialSegments + 1;
  private const float Tolerance = 0.001f;

  [TestCase(TestName = "CreateMesh hides paths with fewer than two points")]
  public void CreateMeshHidesPathsWithFewerThanTwoPoints()
  {
    ArrayMesh empty = MovementLineBuilder.CreateMesh([], cornerRadius: 0.25f, width: 0.2f);
    ArrayMesh single = MovementLineBuilder.CreateMesh(
      [new Vector3(-3.5f, 0.08f, -3.5f)],
      cornerRadius: 0.25f,
      width: 0.2f);

    Assert.Equal(0, empty.GetSurfaceCount());
    Assert.Equal(0, single.GetSurfaceCount());
  }

  [TestCase(TestName = "CreateMesh builds a straight tube spanning its endpoints")]
  public void CreateMeshBuildsStraightTubeSpanningEndpoints()
  {
    float width = 0.2f;
    ArrayMesh mesh = MovementLineBuilder.CreateMesh(
      [new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f)],
      cornerRadius: 0.25f,
      width: width);

    Assert.Equal(1, mesh.GetSurfaceCount());
    Assert.Equal(Mesh.PrimitiveType.Triangles, mesh.SurfaceGetPrimitiveType(0));

    int vertexCount = mesh.SurfaceGetArrayLen(0);
    Assert.Equal(0, vertexCount % RingVertexCount);
    Assert.True(vertexCount / RingVertexCount >= 2); // at least two rings

    Assert.Equal((vertexCount / RingVertexCount - 1) * RadialSegments * 6, mesh.SurfaceGetArrayIndexLen(0));

    Aabb bounds = mesh.GetAabb();
    Assert.True(Mathf.Abs(bounds.Size.X - 1.0f) < Tolerance);
    Assert.True(Mathf.Abs(bounds.Size.Y - (2.0f * width)) < Tolerance);
    Assert.True(Mathf.Abs(bounds.Size.Z - (2.0f * width)) < Tolerance);
  }

  [TestCase(TestName = "CreateMesh UVs advance from 0 to 1 along the path")]
  public void CreateMeshUvsAdvanceAlongThePath()
  {
    ArrayMesh mesh = MovementLineBuilder.CreateMesh(
      [new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 1.0f)],
      cornerRadius: 0.25f,
      width: 0.2f);

    Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
    Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
    int rings = uvs.Length / RingVertexCount;

    for (int s = 0; s < RingVertexCount; s++)
      Assert.True(Mathf.IsEqualApprox(0.0f, uvs[s].X));
    for (int s = 0; s < RingVertexCount; s++)
      Assert.True(Mathf.IsEqualApprox(1.0f, uvs[^(RingVertexCount - s)].X));
    for (int ring = 1; ring < rings; ring++)
      Assert.True(uvs[ring * RingVertexCount].X >= uvs[(ring - 1) * RingVertexCount].X);
  }

  [TestCase(TestName = "CreateMesh rounds corners away from the corner vertex")]
  public void CreateMeshRoundsCornersAwayFromTheCornerVertex()
  {
    Vector3[] path = [new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 1.0f)];

    ArrayMesh rounded = MovementLineBuilder.CreateMesh(path, cornerRadius: 0.25f, width: 0.02f);
    bool hasCenterInsideTheCorner = false;
    float deepestCutDepth = 0.0f;
    foreach (Vector3 center in RingCenters(rounded))
    {
      if (center.X >= 0.995f || center.Z <= 0.005f)
        continue;

      hasCenterInsideTheCorner = true;
      // Perpendicular distance from the corner-cut diagonal S=(0.75,0,0)→E=(1,0,0.25) toward the
      // corner vertex C=(1,0,0) — the fillet's cut depth. A circular-arc fillet of radius 0.25
      // cuts ≈ 0.2929·0.25 ≈ 0.073 deep; the quadratic fillet with handles on C cuts ≈ 0.133.
      float cutDepth = ((center.X - 0.75f) - center.Z) / Mathf.Sqrt2;
      deepestCutDepth = Mathf.Max(deepestCutDepth, cutDepth);
    }

    Assert.True(hasCenterInsideTheCorner);
    Assert.True(deepestCutDepth >= 0.06f && deepestCutDepth <= 0.09f);

    ArrayMesh sharp = MovementLineBuilder.CreateMesh(path, cornerRadius: 0.0f, width: 0.02f);
    bool allCentersOnALeg = true;
    foreach (Vector3 center in RingCenters(sharp))
    {
      if (Mathf.Abs(center.Z) >= 0.005f && Mathf.Abs(center.X - 1.0f) >= 0.005f)
        allCentersOnALeg = false;
    }

    Assert.True(allCentersOnALeg);
  }

  // Rings are consecutive blocks of RingVertexCount vertices; a block's average is the sampled
  // path point because the radial offsets cancel over a full ring.
  private static Vector3[] RingCenters(ArrayMesh mesh)
  {
    Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
    Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();

    Vector3[] centers = new Vector3[vertices.Length / RingVertexCount];
    for (int ring = 0; ring < centers.Length; ring++)
    {
      Vector3 sum = Vector3.Zero;
      for (int vertex = 0; vertex < RingVertexCount; vertex++)
        sum += vertices[(ring * RingVertexCount) + vertex];
      centers[ring] = sum / RingVertexCount;
    }

    return centers;
  }
}
