using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class MovementLineBuilderTest
{
  [TestCase(TestName = "CreateLine hides paths with fewer than two points")]
  public void CreateLineHidesPathsWithFewerThanTwoPoints()
  {
    MeshInstance3D line = MovementLineBuilder.CreateLine([new Vector3(-3.5f, 0.08f, -3.5f)]);

    try
    {
      Assert.False(line.Visible);
      Assert.Equal(0, line.Mesh.GetSurfaceCount());
    }
    finally
    {
      line.Free();
    }
  }

  [TestCase(TestName = "CreateLine returns a configured parentable mesh instance")]
  public void CreateLineReturnsAConfiguredParentableMeshInstance()
  {
    MeshInstance3D line = MovementLineBuilder.CreateLine(
      [new Vector3(-3.5f, 0.08f, -3.5f), new Vector3(-2.5f, 0.08f, -3.5f)],
      Colors.Green,
      cornerRadius: 0.0f,
      width: 0.2f);

    try
    {
      Assert.True(line.Visible);
      Assert.Equal("MovementLine", line.Name.ToString());
      Assert.Equal(1, line.Mesh.GetSurfaceCount());
      ArrayMesh mesh = RequireArrayMesh(line);
      Assert.Equal(Mesh.PrimitiveType.Triangles, mesh.SurfaceGetPrimitiveType(0));
      Assert.True(mesh.SurfaceGetArrayIndexLen(0) > 0);

      StandardMaterial3D material = line.Mesh.SurfaceGetMaterial(0) as StandardMaterial3D;
      Assert.True(material != null);
      Assert.True(material.AlbedoColor.IsEqualApprox(Colors.Green));
      Assert.Equal(BaseMaterial3D.ShadingModeEnum.Unshaded, material.ShadingMode);
      Assert.Equal(BaseMaterial3D.CullModeEnum.Disabled, material.CullMode);
    }
    finally
    {
      line.Free();
    }
  }

  [TestCase(TestName = "CreateLine adds length UVs for shader animation")]
  public void CreateLineAddsLengthUvsForShaderAnimation()
  {
    MeshInstance3D line = MovementLineBuilder.CreateLine(
      [new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 1.0f)],
      cornerRadius: 0.0f,
      width: 0.2f);

    try
    {
      ArrayMesh mesh = RequireArrayMesh(line);
      Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
      Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
      int ringVertexCount = uvs.Length / 3;

      Assert.Equal(mesh.SurfaceGetArrayLen(0), uvs.Length);
      Assert.Equal(0, uvs.Length % 3);
      Assert.True(ringVertexCount >= 4);
      Assert.True(Mathf.IsEqualApprox(0.0f, uvs[0].X));
      Assert.True(Mathf.IsEqualApprox(0.0f, uvs[ringVertexCount - 1].X));
      Assert.True(Mathf.IsEqualApprox(0.5f, uvs[ringVertexCount].X));
      Assert.True(Mathf.IsEqualApprox(0.5f, uvs[(ringVertexCount * 2) - 1].X));
      Assert.True(Mathf.IsEqualApprox(1.0f, uvs[^ringVertexCount].X));
      Assert.True(Mathf.IsEqualApprox(1.0f, uvs[^1].X));
    }
    finally
    {
      line.Free();
    }
  }

  [TestCase(TestName = "CreateLine applies width as the tube radius")]
  public void CreateLineAppliesWidthAsTheTubeRadius()
  {
    MeshInstance3D line = MovementLineBuilder.CreateLine(
      [new Vector3(-3.5f, 0.08f, -3.5f), new Vector3(-2.5f, 0.08f, -3.5f)],
      cornerRadius: 0.0f,
      width: 0.2f);

    try
    {
      Aabb bounds = line.Mesh.GetAabb();
      Assert.True(Mathf.IsEqualApprox(1.0f, bounds.Size.X));
      Assert.True(Mathf.IsEqualApprox(0.4f, bounds.Size.Y));
      Assert.True(Mathf.IsEqualApprox(0.4f, bounds.Size.Z));
    }
    finally
    {
      line.Free();
    }
  }

  [TestCase(TestName = "CreateLine adds tube rings for rounded turns")]
  public void CreateLineAddsTubeRingsForRoundedTurns()
  {
    MeshInstance3D line = MovementLineBuilder.CreateLine(
      [new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 1.0f)],
      cornerRadius: 0.25f,
      cornerSegments: 4,
      width: 0.2f);

    try
    {
      Assert.Equal(1, line.Mesh.GetSurfaceCount());
      ArrayMesh mesh = RequireArrayMesh(line);
      int roundedPathPointCount = 7;
      int ringVertexCount = mesh.SurfaceGetArrayLen(0) / roundedPathPointCount;

      Assert.Equal(0, mesh.SurfaceGetArrayLen(0) % roundedPathPointCount);
      Assert.True(ringVertexCount >= 9);
      Assert.Equal((roundedPathPointCount - 1) * (ringVertexCount - 1) * 6, mesh.SurfaceGetArrayIndexLen(0));
    }
    finally
    {
      line.Free();
    }
  }

  [TestCase(TestName = "UpdateLine replaces the existing mesh with new geometry")]
  public void UpdateLineReplacesTheExistingMeshWithNewGeometry()
  {
    MeshInstance3D line = MovementLineBuilder.CreateLine(
      [new Vector3(-3.5f, 0.08f, -3.5f), new Vector3(-2.5f, 0.08f, -3.5f)],
      cornerRadius: 0.0f,
      width: 0.2f);

    try
    {
      MovementLineBuilder.UpdateLine(
        ref line,
        [new Vector3(-3.5f, 0.08f, -3.5f), new Vector3(-1.5f, 0.08f, -3.5f)],
        cornerRadius: 0.0f,
        width: 0.4f);

      Assert.True(line.Visible);
      Assert.Equal(1, line.Mesh.GetSurfaceCount());

      Aabb bounds = line.Mesh.GetAabb();
      Assert.True(Mathf.IsEqualApprox(2.0f, bounds.Size.X));
      Assert.True(Mathf.IsEqualApprox(0.8f, bounds.Size.Y));
      Assert.True(Mathf.IsEqualApprox(0.8f, bounds.Size.Z));
    }
    finally
    {
      line.Free();
    }
  }

  [TestCase(TestName = "UpdateLine hides an existing line when the path becomes invalid")]
  public void UpdateLineHidesAnExistingLineWhenThePathBecomesInvalid()
  {
    MeshInstance3D line = MovementLineBuilder.CreateLine(
      [new Vector3(-3.5f, 0.08f, -3.5f), new Vector3(-2.5f, 0.08f, -3.5f)],
      cornerRadius: 0.0f,
      width: 0.2f);

    try
    {
      Assert.True(line.Visible);

      MovementLineBuilder.UpdateLine(ref line, [new Vector3(-3.5f, 0.08f, -3.5f)]);

      Assert.False(line.Visible);
      Assert.Equal(0, line.Mesh.GetSurfaceCount());
    }
    finally
    {
      line.Free();
    }
  }

  private static ArrayMesh RequireArrayMesh(MeshInstance3D line)
  {
    ArrayMesh mesh = line.Mesh as ArrayMesh;
    Assert.True(mesh != null);
    return mesh;
  }
}
