using FunProject.Models;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class GraceCrowneAssetTest
{
  private const string Scene = "res://scenes/models/GraceCrowne/GraceCrowne.scn";
  private const string Skeleton = "Model/rig_D/GeneralSkeleton/";
  private const string Body = Skeleton + "grace_crowne";
  private const string Hair = Skeleton + "grace_crowne_hair";

  [TestCase]
  public void EveryOutfitCoordinatesItsGarmentAndPreservedFacialBodyMask()
  {
    using var fixture = ModelFixture.FromScene(Scene);
    string[] names = ["suit", "exposed", "unzipped", "zipped"];
    string[] garments = ["grace_crowne_suit", "grace_crowne_suit_exposed", "grace_crowne_suit_unzipped", "grace_crowne_suit_zipped"];
    var source = (ArrayMesh)fixture.Model.GetNode<MeshInstance3D>(Body).Mesh;
    int fullTriangles = TestData.VisibleTriangles(source);
    for (int outfit = 0; outfit < names.Length; outfit++)
    {
      fixture.Model.OutfitIndex = outfit;
      Assert.MaskRegions(fixture.Model, Body, names[outfit]);
      for (int garment = 0; garment < garments.Length; garment++)
        Assert.Equal(garment == outfit, fixture.Model.GetNode<MeshInstance3D>(Skeleton + garments[garment]).Visible);
      ArrayMesh masked = fixture.Model.MaskRenderMesh(Body)!;
      Assert.True(TestData.VisibleTriangles(masked) < fullTriangles, "The garment did not hide any body geometry.");
      Assert.Equal(73, masked.GetBlendShapeCount(), "Clothing masks lost the facial morphs.");
      fixture.Model.SetPiece("Suit", false);
      Assert.MaskRegions(fixture.Model, Body);
      Assert.Equal(fullTriangles, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(Body)));
      Assert.False(fixture.Model.GetNode<MeshInstance3D>(Skeleton + garments[outfit]).Visible);
      fixture.Model.SetPiece("Suit", true);
    }
  }

  [TestCase]
  public void SourceMaterialsStayInstanceLocalAcrossWardrobeMaskRebuilds()
  {
    using var first = ModelFixture.FromScene(Scene);
    using var second = ModelFixture.FromScene(Scene);
    var firstBody = first.Model.GetNode<MeshInstance3D>(Body);
    var secondBody = second.Model.GetNode<MeshInstance3D>(Body);
    var firstSkin = firstBody.GetActiveMaterial(0) as ShaderMaterial;
    var secondSkin = secondBody.GetActiveMaterial(0) as ShaderMaterial;
    Assert.True(firstSkin is not null, "Grace skin lost its live source shader.");
    Assert.True(secondSkin is not null);
    Assert.True(firstSkin!.GetInstanceId() != secondSkin!.GetInstanceId(), "Material edits leak between Grace instances.");
    double original = secondSkin.GetShaderParameter("skin_exponent").AsDouble();
    firstSkin.SetShaderParameter("skin_exponent", 2.0);
    first.Model.OutfitIndex = 2;
    var rebuiltSkin = first.Model.GetNode<MeshInstance3D>(Body).GetActiveMaterial(0) as ShaderMaterial;
    Assert.Equal(2.0, rebuiltSkin!.GetShaderParameter("skin_exponent").AsDouble(), "Changing the outfit discarded the local material.");
    Assert.Equal(original, secondSkin.GetShaderParameter("skin_exponent").AsDouble());
    Assert.Equal(firstSkin.Shader.GetInstanceId(), secondSkin.Shader.GetInstanceId(), "Immutable shaders should remain shared.");
  }

  [TestCase]
  public void AccessoriesUseExistingPieceControlsWithoutLeakingBetweenInstances()
  {
    using var first = ModelFixture.FromScene(Scene);
    using var second = ModelFixture.FromScene(Scene);
    first.Model.SetPiece("Goggles", true);
    Assert.True(first.Model.GetNode<MeshInstance3D>(Skeleton + "grace_crowne_goggles").Visible);
    Assert.MaskRegions(first.Model, Hair, "goggles");
    Assert.Equal(0, TestData.VisibleTriangles(first.Model.MaskRenderMesh(Hair)));
    Assert.MaskRegions(second.Model, Hair);
    Assert.False(second.Model.GetNode<MeshInstance3D>(Skeleton + "grace_crowne_goggles").Visible);
    first.Model.SetPiece("Mask", true);
    first.Model.SetPiece("Headband", false);
    first.Model.SetPiece("Gadgets", false);
    Assert.True(first.Model.GetNode<MeshInstance3D>(Skeleton + "grace_crowne_mask").Visible);
    Assert.False(first.Model.GetNode<MeshInstance3D>(Skeleton + "Headband").Visible);
    Assert.False(first.Model.GetNode<MeshInstance3D>(Skeleton + "Gadgets").Visible);
    first.Model.ClothingEnabled = false;
    Assert.MaskRegions(first.Model, Body);
    Assert.MaskRegions(first.Model, Hair);
    Assert.False(first.Model.GetNode<MeshInstance3D>(Skeleton + "grace_crowne_mask").Visible);
    first.Model.ClothingEnabled = true;
    Assert.MaskRegions(first.Model, Body, "suit");
    Assert.MaskRegions(first.Model, Hair, "goggles");
    Assert.True(first.Model.GetNode<MeshInstance3D>(Skeleton + "grace_crowne_mask").Visible);
  }
}
