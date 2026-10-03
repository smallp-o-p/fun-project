using FunProject.Battle;
using Godot;

// One map-local grid contract shared by authoring and presentation. Gameplay stays integer-based.
public sealed class BoardCoordinates
{
  public static readonly BoardCoordinates UnitGrid = new(new BattleMapData(), Transform3D.Identity);
  public Vector3 Origin { get; }
  public Vector3 CellSize { get; }
  public Transform3D MapTransform { get; }
  public Vector3 Up => MapTransform.Basis.Y;

  public BoardCoordinates(BattleMapData map, Transform3D mapTransform)
  {
    Origin = map.GridOrigin;
    CellSize = new(map.CellWidth, map.LevelHeight, map.CellWidth);
    MapTransform = mapTransform;
  }

  public Vector3 MapToWorld(Vector3 point) => MapTransform * point;
  public Vector3 WorldToMap(Vector3 point) => MapTransform.AffineInverse() * point;
  public Vector3 TileToWorldCenter(Vector3I tile) =>
    MapToWorld(Origin + (new Vector3(tile.X, tile.Y, tile.Z) + new Vector3(0.5f, 0, 0.5f)) * CellSize);
  public Vector3 TileToWorldVolumeCenter(Vector3I tile) => TileToWorldCenter(tile) + Up * CellSize.Y / 2;

  public Vector3I WorldToTile(Vector3 world)
  {
    var cell = (WorldToMap(world) - Origin) / CellSize;
    return new(Mathf.FloorToInt(cell.X), Mathf.RoundToInt(cell.Y), Mathf.FloorToInt(cell.Z));
  }

  public Vector3I WorldVolumeToTile(Vector3 world)
  {
    var cell = (WorldToMap(world) - Origin) / CellSize;
    return new(Mathf.FloorToInt(cell.X), Mathf.FloorToInt(cell.Y), Mathf.FloorToInt(cell.Z));
  }
}
