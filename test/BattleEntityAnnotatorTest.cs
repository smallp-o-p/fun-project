using GdUnit4;
using Godot;
using System;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleEntityAnnotatorTest
{
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  public void PaintedItemsSupplyIndependentGameplayDefaults(int item)
  {
    var annotator = AutoFree(TestData.MakeEntityAnnotator())!;
    annotator.Grid.SetCellItem(Godot.Vector3I.Zero, item);
    annotator.Grid.SetCellItem(Godot.Vector3I.Right, item);
    var footprint = annotator.BuildFootprint();
    var first = footprint[Godot.Vector3I.Zero];
    var second = footprint[Godot.Vector3I.Right];
    Assert.Equal(item == 1, first.HasFloor);
    Assert.Equal(item == 2, first.BlocksMovement);
    Assert.Equal(item == 2, first.BlocksLineOfSight);
    Assert.False(first == second);
    first.HasFloor = !first.HasFloor;
    Assert.Equal(item == 1, second.HasFloor);
    Assert.Equal(item == 1, annotator.BuildFootprint()[Godot.Vector3I.Zero].HasFloor);
    Assert.Equal(0, annotator.Annotations.Count);
  }

  [TestCase]
  public void AssignedPalettePreservesPaintAndExplicitFlags()
  {
    var flags = new BattleFootprintData { HasFloor = true };
    var annotator = AutoFree(TestData.MakeEntityAnnotator(new() { [Godot.Vector3I.Zero] = flags }))!;
    annotator.Grid.SetCellItem(Godot.Vector3I.Zero, 2);
    int orientation = annotator.Grid.GetOrthogonalIndexFromBasis(new Basis(Vector3.Up, Mathf.Pi / 2));
    annotator.Grid.SetCellItem(Godot.Vector3I.Right, 1, orientation);

    annotator.Markers = (MeshLibrary)annotator.Markers!.Duplicate(true);

    Assert.Equal(2, annotator.Grid.GetUsedCells().Count);
    Assert.Equal(2, annotator.Grid.GetCellItem(Godot.Vector3I.Zero));
    Assert.Equal(1, annotator.Grid.GetCellItem(Godot.Vector3I.Right));
    Assert.Equal(orientation, annotator.Grid.GetCellItemOrientation(Godot.Vector3I.Right));
    var footprint = annotator.BuildFootprint();
    Assert.False(footprint[Godot.Vector3I.Zero] == flags);
    Assert.False(footprint[Godot.Vector3I.Zero].BlocksMovement);
    Assert.True(footprint[Godot.Vector3I.Right].HasFloor);
    Assert.Equal(1, annotator.Annotations.Count);
  }

  [TestCase]
  public void ExactlyOneOccupiedCellIsRequired()
  {
    var annotator = AutoFree(TestData.MakeEntityAnnotator())!;
    annotator.Grid.SetCellItem(Godot.Vector3I.Zero, 0);
    Assert.False(annotator.TrySelectedCell([], out _));
    Assert.False(annotator.TrySelectedCell([Godot.Vector3I.Zero, Godot.Vector3I.Right], out _));
    Assert.False(annotator.TrySelectedCell([Godot.Vector3I.Right], out _));
    Assert.True(annotator.TrySelectedCell([Godot.Vector3I.Zero], out var cell));
    Assert.Equal(Godot.Vector3I.Zero, cell);
  }

  [TestCase]
  public void ErasedCellFlagsStayAtTheirCoordinateAndApplyWhenRepainted()
  {
    var flags = new BattleFootprintData { HasFloor = true };
    var annotator = AutoFree(TestData.MakeEntityAnnotator(new() { [Godot.Vector3I.Zero] = flags }))!;
    annotator.Grid.SetCellItem(Godot.Vector3I.Zero, -1);
    annotator.Grid.SetCellItem(Godot.Vector3I.Right, 2);
    var footprint = annotator.BuildFootprint();
    Assert.Equal(1, footprint.Count);
    Assert.False(footprint.ContainsKey(Godot.Vector3I.Zero));
    Assert.False(footprint[Godot.Vector3I.Right].HasFloor);
    Assert.True(footprint[Godot.Vector3I.Right].BlocksMovement);
    Assert.True(annotator.Annotations[Godot.Vector3I.Zero] == flags);

    annotator.Grid.SetCellItem(Godot.Vector3I.Zero, 2);
    footprint = annotator.BuildFootprint();
    Assert.Equal(2, footprint.Count);
    Assert.False(footprint[Godot.Vector3I.Zero] == flags);
    Assert.True(footprint[Godot.Vector3I.Zero].HasFloor);
    Assert.False(footprint[Godot.Vector3I.Zero].BlocksMovement);
  }

  [TestCase]
  public void SavedAnnotationsAndMarkersRoundTrip()
  {
    var annotator = AutoFree(TestData.MakeEntityAnnotator(new()
    {
      [Godot.Vector3I.Zero] = new() { BlocksMovement = true, CoverAmount = 45 }
    }))!;
    annotator.Grid.SetCellItem(Godot.Vector3I.Right, 1);
    using var packed = new PackedScene();
    Assert.Equal(Error.Ok, packed.Pack(annotator));
    const string path = "user://annotation_roundtrip.tscn";
    Assert.Equal(Error.Ok, ResourceSaver.Save(packed, path));
    var loaded = AutoFree(ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<BattleEntityAnnotator>())!;
    var footprint = loaded.BuildFootprint();
    Assert.True(footprint[Godot.Vector3I.Zero].BlocksMovement);
    Assert.Equal(45, footprint[Godot.Vector3I.Zero].CoverAmount);
    Assert.True(footprint[Godot.Vector3I.Right].HasFloor);
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void InvalidAnnotationGridSettingsAreRejected(int scenario)
  {
    var annotator = AutoFree(TestData.MakeEntityAnnotator(TestData.MakeFloorFootprint(1, 1)))!;
    if (scenario == 0) annotator.Grid.Position = Vector3.Right;
    if (scenario == 1) annotator.Grid.CellSize = Vector3.One * 2;
    if (scenario == 2) annotator.Grid.CellCenterY = false;
    if (scenario == 3) annotator.Grid.SetCellItem(Godot.Vector3I.Zero, 0,
      annotator.Grid.GetOrthogonalIndexFromBasis(new Basis(Vector3.Right, Mathf.Pi / 2)));
    Assert.True(annotator.Validate().IsSome);
    Assert.Throws<InvalidOperationException>(() => annotator.BuildFootprint());
  }
}
