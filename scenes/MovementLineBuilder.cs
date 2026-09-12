using Godot;
using System.Collections.Generic;

public static class MovementLineBuilder
{
  private const float RingSpacing = 0.025f;
  private const int RadialSegments = 64;
  private const float DirectionEpsilon = 0.0001f;

  public static ArrayMesh CreateMesh(IReadOnlyList<Vector3> points, float cornerRadius, float width)
  {
    ArrayMesh mesh = new();
    if (points.Count < 2)
      return mesh;

    Curve3D curve = CreateCurve(points, cornerRadius);
    curve.BakeInterval = RingSpacing;
    float bakedLength = curve.GetBakedLength();
    if (bakedLength <= DirectionEpsilon)
      return mesh;

    int rings = Mathf.Max(2, (int)Mathf.Ceil(bakedLength / RingSpacing));
    int ringVertexCount = RadialSegments + 1;
    Vector3[] vertices = new Vector3[rings * ringVertexCount];
    Vector3[] normals = new Vector3[vertices.Length];
    Vector2[] uvs = new Vector2[vertices.Length];
    int[] indices = new int[(rings - 1) * RadialSegments * 6];

    for (int ring = 0; ring < rings; ring++)
    {
      float t = ring / (float)(rings - 1);
      Transform3D xf = curve.SampleBakedWithRotation(t * bakedLength, cubic: true);

      for (int segment = 0; segment <= RadialSegments; segment++)
      {
        float angle = Mathf.Tau * segment / RadialSegments;
        Vector3 ringNormal = (xf.Basis.X * Mathf.Cos(angle)) + (xf.Basis.Y * Mathf.Sin(angle));
        int vertexIndex = (ring * ringVertexCount) + segment;

        vertices[vertexIndex] = xf.Origin + (ringNormal * width);
        normals[vertexIndex] = ringNormal;
        uvs[vertexIndex] = new Vector2(t, segment / (float)RadialSegments);
      }
    }

    int index = 0;
    for (int ring = 0; ring < rings - 1; ring++)
    {
      int currentRing = ring * ringVertexCount;
      int nextRing = (ring + 1) * ringVertexCount;

      for (int segment = 0; segment < RadialSegments; segment++)
      {
        int current = currentRing + segment;
        int currentNext = current + 1;
        int next = nextRing + segment;
        int nextNext = next + 1;

        indices[index++] = current;
        indices[index++] = currentNext;
        indices[index++] = next;

        indices[index++] = currentNext;
        indices[index++] = nextNext;
        indices[index++] = next;
      }
    }

    Godot.Collections.Array arrays = [];
    arrays.Resize((int)Mesh.ArrayType.Max);
    arrays[(int)Mesh.ArrayType.Vertex] = vertices;
    arrays[(int)Mesh.ArrayType.Normal] = normals;
    arrays[(int)Mesh.ArrayType.TexUV] = uvs;
    arrays[(int)Mesh.ArrayType.Index] = indices;
    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    return mesh;
  }

  private static Curve3D CreateCurve(IReadOnlyList<Vector3> points, float cornerRadius)
  {
    SysColGeneric.List<Vector3> distinct = [];
    foreach (Vector3 point in points)
    {
      if (distinct.Count == 0 || !point.IsEqualApprox(distinct[^1]))
        distinct.Add(point);
    }

    Curve3D curve = new();
    curve.AddPoint(distinct[0]);

    for (int i = 1; i < distinct.Count - 1; i++)
    {
      Vector3 incomingDirection = distinct[i] - distinct[i - 1];
      Vector3 outgoingDirection = distinct[i + 1] - distinct[i];
      float incomingLength = incomingDirection.Length();
      float outgoingLength = outgoingDirection.Length();
      float pull = Mathf.Min(cornerRadius, Mathf.Min(incomingLength, outgoingLength) / 2.0f);
      Vector3 corner = distinct[i];
      if (pull <= DirectionEpsilon)
      {
        curve.AddPoint(corner);
        continue;
      }

      // Cut the corner: S/E sit pull away from the vertex while the start/end handles keep the
      // surrounding legs straight. The (4/3)·tan(θ/4) handle factor is the standard circular-arc
      // cubic-bezier approximation (0.5523 for a 90° turn), so the S→E bezier hugs the true fillet
      // arc instead of the shallower quadratic whose middle controls land on the corner vertex.
      Vector3 inDirection = incomingDirection / incomingLength;
      Vector3 outDirection = outgoingDirection / outgoingLength;
      float turnAngle = Mathf.Acos(Mathf.Clamp(inDirection.Dot(outDirection), -1.0f, 1.0f));
      float handleLength = Mathf.Min(pull, pull * (4.0f / 3.0f) * Mathf.Tan(turnAngle / 4.0f));
      curve.AddPoint(corner - (inDirection * pull), @in: -inDirection * handleLength, @out: inDirection * handleLength);
      curve.AddPoint(corner + (outDirection * pull), @in: -outDirection * handleLength, @out: outDirection * handleLength);
    }

    curve.AddPoint(distinct[^1]);
    return curve;
  }
}
