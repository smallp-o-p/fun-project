using Godot;
using System.Collections.Generic;

public static class MovementLineBuilder
{
  private const float DirectionEpsilon = 0.0001f;
  private const float DefaultWidth = 0.06f;
  private const float DefaultCornerRadius = 0.25f;
  private const int DefaultCornerSegments = 16;
  private const int DefaultTubeRadialSegments = 64;
  private static readonly Color DefaultColor = new(0.25f, 0.75f, 1.0f, 1.0f);

  public static MeshInstance3D CreateLine(
    IReadOnlyList<Vector3> points,
    float cornerRadius = DefaultCornerRadius,
    int cornerSegments = 16,
    float width = DefaultWidth)
  {
    return CreateLine(points, DefaultColor, cornerRadius, cornerSegments, width);
  }

  public static MeshInstance3D CreateLine(
    IReadOnlyList<Vector3> points,
    Color color,
    float cornerRadius = DefaultCornerRadius,
    int cornerSegments = DefaultCornerSegments,
    float width = DefaultWidth)
  {
    ArrayMesh mesh = CreateMesh(points, cornerRadius, cornerSegments, width);
    StandardMaterial3D material = new()
    {
      AlbedoColor = color,
      ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
      CullMode = BaseMaterial3D.CullModeEnum.Disabled
    };

    if (mesh.GetSurfaceCount() > 0)
      mesh.SurfaceSetMaterial(0, material);

    var meshInstance = new MeshInstance3D
    {
      Name = "MovementLine",
      Mesh = mesh,
      CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
      Visible = mesh.GetSurfaceCount() > 0
    };

    return meshInstance;

  }

  public static void UpdateLine(
    ref MeshInstance3D line,
    IReadOnlyList<Vector3> points,
    float cornerRadius = DefaultCornerRadius,
    int cornerSegments = DefaultCornerSegments,
    float width = DefaultWidth)
  {
    ArrayMesh mesh = CreateMesh(points, cornerRadius, cornerSegments, width);
    line.Mesh = mesh;
    line.Visible = mesh.GetSurfaceCount() > 0;
  }

  private static ArrayMesh CreateMesh(
    IReadOnlyList<Vector3> points,
    float cornerRadius = 0.25f,
    int cornerSegments = 16,
    float width = DefaultWidth)
  {
    ArrayMesh mesh = new();
    Vector3[] centerPath = CreateRoundedPath(points, cornerRadius, cornerSegments);
    (Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices) =
      CreateTubeGeometry(centerPath, width, DefaultTubeRadialSegments);

    if (vertices.Length == 0 || indices.Length == 0)
      return mesh;

    Godot.Collections.Array arrays = [];
    arrays.Resize((int)Mesh.ArrayType.Max);
    arrays[(int)Mesh.ArrayType.Vertex] = vertices;
    arrays[(int)Mesh.ArrayType.Normal] = normals;
    arrays[(int)Mesh.ArrayType.TexUV] = uvs;
    arrays[(int)Mesh.ArrayType.Index] = indices;
    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    return mesh;
  }

  private static Vector3[] CreateRoundedPath(
    IReadOnlyList<Vector3> controlPoints,
    float cornerRadius,
    int cornerSegments)
  {
    if (controlPoints.Count == 0)
      return [];

    if (controlPoints.Count < 3 || cornerRadius <= 0.0f || cornerSegments <= 0)
      return [.. controlPoints];

    List<Vector3> roundedPoints = new(controlPoints.Count + ((controlPoints.Count - 2) * cornerSegments));

    AddPointIfDistinct(roundedPoints, controlPoints[0]);
    for (int i = 1; i < controlPoints.Count - 1; i++)
    {
      Vector3 previous = controlPoints[i - 1];
      Vector3 current = controlPoints[i];
      Vector3 next = controlPoints[i + 1];
      if (!TryAppendRoundedCorner(roundedPoints, previous, current, next, cornerRadius, cornerSegments))
        AddPointIfDistinct(roundedPoints, current);
    }

    AddPointIfDistinct(roundedPoints, controlPoints[^1]);
    return [.. roundedPoints];
  }

  private static (Vector3[] Vertices, Vector3[] Normals, Vector2[] Uvs, int[] Indices) CreateTubeGeometry(
    Vector3[] centerPath,
    float radius,
    int radialSegments)
  {
    if (centerPath.Length < 2 || radius <= 0.0f || radialSegments < 3)
      return ([], [], [], []);

    int ringVertexCount = radialSegments + 1;
    Vector3[] vertices = new Vector3[centerPath.Length * ringVertexCount];
    Vector3[] normals = new Vector3[vertices.Length];
    Vector2[] uvs = new Vector2[vertices.Length];
    int[] indices = new int[(centerPath.Length - 1) * radialSegments * 6];
    float[] distances = CreatePathDistances(centerPath);
    float totalDistance = distances[^1];

    for (int i = 0; i < centerPath.Length; i++)
    {
      Vector3 tangent = GetPathTangent(centerPath, i);
      (Vector3 side, Vector3 up) = CreateTubeFrame(tangent);
      float u = totalDistance > 0.0f ? distances[i] / totalDistance : 0.0f;

      for (int segment = 0; segment <= radialSegments; segment++)
      {
        float angle = Mathf.Tau * segment / radialSegments;
        Vector3 ringNormal = (side * Mathf.Cos(angle)) + (up * Mathf.Sin(angle));
        int vertexIndex = (i * ringVertexCount) + segment;

        vertices[vertexIndex] = centerPath[i] + (ringNormal * radius);
        normals[vertexIndex] = ringNormal;
        uvs[vertexIndex] = new Vector2(u, (float)segment / radialSegments);
      }
    }

    int index = 0;
    for (int i = 0; i < centerPath.Length - 1; i++)
    {
      int currentRing = i * ringVertexCount;
      int nextRing = (i + 1) * ringVertexCount;

      for (int segment = 0; segment < radialSegments; segment++)
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

    return (vertices, normals, uvs, indices);
  }

  private static float[] CreatePathDistances(Vector3[] centerPath)
  {
    float[] distances = new float[centerPath.Length];

    for (int i = 1; i < centerPath.Length; i++)
    {
      distances[i] = distances[i - 1] + centerPath[i].DistanceTo(centerPath[i - 1]);
    }

    return distances;
  }

  private static (Vector3 Side, Vector3 Up) CreateTubeFrame(Vector3 tangent)
  {
    if (tangent.Length() <= DirectionEpsilon)
      tangent = Vector3.Forward;

    tangent = tangent.Normalized();
    Vector3 referenceUp = Mathf.Abs(tangent.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
    Vector3 side = referenceUp.Cross(tangent).Normalized();
    Vector3 up = tangent.Cross(side).Normalized();
    return (side, up);
  }

  private static Vector3 GetPathTangent(IReadOnlyList<Vector3> centerPath, int pointIndex)
  {
    if (centerPath == null || centerPath.Count < 2)
      return Vector3.Forward;
    if (pointIndex <= 0)
      return centerPath[1] - centerPath[0];
    if (pointIndex >= centerPath.Count - 1)
      return centerPath[^1] - centerPath[^2];

    return centerPath[pointIndex + 1] - centerPath[pointIndex - 1];
  }

  private static bool TryAppendRoundedCorner(
    List<Vector3> roundedPoints,
    Vector3 previous,
    Vector3 current,
    Vector3 next,
    float cornerRadius,
    int cornerSegments)
  {
    Vector3 incoming = current - previous;
    Vector3 outgoing = next - current;
    float incomingLength = incoming.Length();
    float outgoingLength = outgoing.Length();
    if (incomingLength <= DirectionEpsilon || outgoingLength <= DirectionEpsilon)
      return false;

    Vector3 incomingDirection = incoming / incomingLength;
    Vector3 outgoingDirection = outgoing / outgoingLength;
    float turnDot = Mathf.Clamp(incomingDirection.Dot(outgoingDirection), -1.0f, 1.0f);
    if (Mathf.Abs(turnDot) >= 1.0f - DirectionEpsilon)
      return false;

    float turnAngle = Mathf.Acos(turnDot);
    float tangentScale = Mathf.Tan(turnAngle * 0.5f);
    if (tangentScale <= DirectionEpsilon)
      return false;

    float tangentDistance = Mathf.Min(
      cornerRadius * tangentScale,
      Mathf.Min(incomingLength * 0.5f, outgoingLength * 0.5f));
    if (tangentDistance <= DirectionEpsilon)
      return false;

    float effectiveRadius = tangentDistance / tangentScale;
    if (effectiveRadius <= DirectionEpsilon)
      return false;

    Vector3 cornerStart = current - (incomingDirection * tangentDistance);
    Vector3 cornerEnd = current + (outgoingDirection * tangentDistance);
    Vector3 turnAxis = incomingDirection.Cross(outgoingDirection);
    if (turnAxis.Length() <= DirectionEpsilon)
      return false;

    turnAxis = turnAxis.Normalized();
    Vector3 startToCenterDirection = turnAxis.Cross(incomingDirection).Normalized();
    Vector3 arcCenter = cornerStart + (startToCenterDirection * effectiveRadius);
    Vector3 arcStartOffset = cornerStart - arcCenter;

    AddPointIfDistinct(roundedPoints, cornerStart);
    for (int segment = 1; segment < cornerSegments; segment++)
    {
      float angle = turnAngle * segment / cornerSegments;
      AddPointIfDistinct(roundedPoints, arcCenter + arcStartOffset.Rotated(turnAxis, angle));
    }

    AddPointIfDistinct(roundedPoints, cornerEnd);
    return true;
  }

  private static void AddPointIfDistinct(List<Vector3> points, Vector3 point)
  {
    if (points.Count == 0 || !points[^1].IsEqualApprox(point))
      points.Add(point);
  }
}
