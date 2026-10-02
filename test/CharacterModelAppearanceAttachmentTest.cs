using System;
using FunProject.Models;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class CharacterModelAppearanceAttachmentTest
{
  // ------------------------------------------------------------------
  // Per-instance material isolation
  // ------------------------------------------------------------------

  [TestCase]
  public void IsolationLeavesAuthoredMaterialsUntouchedWithoutRuntimeProcessing()
  {
    using var fixture = ModelFixture.WithAppearance();
    var material = (ShaderMaterial)fixture.Body.GetSurfaceOverrideMaterial(0)!;
    Assert.False(ModelFixture.SameNative(material, fixture.SharedMaterial));
    Assert.False(ModelFixture.SameNative(fixture.Outline, fixture.SharedMaterial!.NextPass));
    fixture.Outline.SetShaderParameter("width_scale", 2.5);
    Assert.Equal(1.0, ((ShaderMaterial)fixture.SharedMaterial.NextPass!)
      .GetShaderParameter("width_scale").AsDouble());
    Assert.False(fixture.Model.IsProcessing());
  }

  // Isolation carries authored outline settings per instance: a non-default
  // width, a disabled pass, and a surface authored without any pass survive
  // as authored.
  [TestCase]
  public void IsolationCarriesAuthoredOutlineSettings()
  {
    using var widthFixture = ModelFixture.WithAppearance(start: false,
      authorMaterial: material => ((ShaderMaterial)material.NextPass!).SetShaderParameter("width_scale", 2.5));
    widthFixture.Start();
    Assert.Equal(2.5, widthFixture.Outline.GetShaderParameter("width_scale").AsDouble());
    Assert.Equal(2.5, ((ShaderMaterial)widthFixture.SharedMaterial!.NextPass!)
      .GetShaderParameter("width_scale").AsDouble());

    using var disabledFixture = ModelFixture.WithAppearance(start: false,
      authorMaterial: material => ((ShaderMaterial)material.NextPass!).SetShaderParameter("enabled", false));
    disabledFixture.Start();
    Assert.False(disabledFixture.Outline.GetShaderParameter("enabled").AsBool());
    Assert.False(((ShaderMaterial)disabledFixture.SharedMaterial!.NextPass!)
      .GetShaderParameter("enabled").AsBool());

    using var passlessFixture = ModelFixture.WithAppearance(start: false,
      authorMaterial: material => material.NextPass = null);
    passlessFixture.Start();
    Assert.That(passlessFixture.Body.GetSurfaceOverrideMaterial(0)!.NextPass is null,
      "A surface authored without an outline pass must stay without one after isolation.");
    Assert.That(passlessFixture.SharedMaterial!.NextPass is null);
  }

  [TestCase]
  public void RepeatedInitializationKeepsIsolatedMaterials()
  {
    using var fixture = ModelFixture.WithAppearance(withMask: true);
    ulong surfaceId = fixture.Body.GetSurfaceOverrideMaterial(0)!.GetInstanceId();
    ulong outlineId = fixture.Outline.GetInstanceId();
    fixture.Model.Initialize();
    Assert.Equal(surfaceId, fixture.Body.GetSurfaceOverrideMaterial(0)!.GetInstanceId());
    Assert.Equal(outlineId, fixture.Outline.GetInstanceId());
    Assert.False(fixture.Model.IsProcessing());
  }

  // ------------------------------------------------------------------
  // Mask rendering against isolated materials
  // ------------------------------------------------------------------

  [TestCase]
  public void MaskRenderUsesIsolatedOutlineWeights()
  {
    // The first mask render uses overrides already localized by instantiation,
    // even though the masked body lives inside a nested mesh wrapper.
    using var fixture = ModelFixture.WithAppearance(withMask: true);
    TestData.SelectMaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    Assert.MaskRegions(fixture.Model, fixture.MaskPath, "Mask0");
    Assert.Equal(1, TestData.VisibleTriangles(fixture.Model.MaskRenderMesh(fixture.MaskPath)));
    Assert.True(fixture.Outline.GetShaderParameter("vertex_weights").VariantType
      == Variant.Type.Object);
    Assert.False(((ShaderMaterial)fixture.SharedMaterial!.NextPass!)
      .GetShaderParameter("vertex_weights").VariantType == Variant.Type.Object);
  }

}
