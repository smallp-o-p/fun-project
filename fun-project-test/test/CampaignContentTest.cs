using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class CampaignContentTest
{
  private static readonly string MainRoot = ResolveMainRoot();

  [TestCase(TestName = "Authored equipment and mods declare their expected resource classes")]
  public void AuthoredContentFilesExist()
  {
    (string path, string scriptClass)[] expectedFiles =
    [
      ("resources/items/vest.tres", "EquippableItemData"),
      ("resources/items/frag_grenade.tres", "EquippableItemData"),
      ("resources/weapons/combat_rifle.tres", "FirearmWeaponData"),
      ("resources/mods/reflex_chip.tres", "MultiStatMod"),
      ("resources/mods/cardio_stims.tres", "MultiStatMod")
    ];

    foreach ((string relativePath, string scriptClass) in expectedFiles)
    {
      string path = Path.Combine(MainRoot, relativePath);
      Assert.True(File.Exists(path));
      Assert.True(File.ReadAllText(path).Contains(
        $"script_class=\"{scriptClass}\"",
        StringComparison.Ordinal));
    }
  }

  [TestCase(TestName = "Combat rifle authors two mod slots while service pistol remains slotless")]
  public void AuthoredWeaponModSlots()
  {
    string rifle = ReadResource("resources/weapons/combat_rifle.tres");
    string pistol = ReadResource("resources/weapons/service_pistol.tres");
    string modSlotsScriptId = FindExtResourceId(rifle, "scripts/items/capabilities/ModSlotsCapabilityData.cs");
    string modSlotsBlock = FindSubResourceByScript(rifle, modSlotsScriptId);
    string modSlotsId = FindSubResourceId(modSlotsBlock);
    string rifleResource = MainResourceBlock(rifle);

    Assert.True(modSlotsBlock.Contains("SlotCount = 2", StringComparison.Ordinal));
    Assert.True(rifleResource.Contains("Capabilities =", StringComparison.Ordinal));
    Assert.True(rifleResource.Contains($"SubResource(\"{modSlotsId}\")", StringComparison.Ordinal));
    Assert.False(pistol.Contains("ModSlotsCapabilityData.cs", StringComparison.Ordinal));
  }

  [TestCase(TestName = "Extended magwell authors an additive ammunition modifier")]
  public void ExtendedMagwellAddsAmmunition()
  {
    string magwell = ReadResource("resources/mods/extended_magwell.tres");
    string modifierScriptId = FindExtResourceId(magwell, "scripts/stats/StatModifier.cs");
    string ammunitionModScriptId = FindExtResourceId(magwell, "scripts/stats/concrete_mods/AmmunitionStatMod.cs");
    string modifierBlock = FindSubResourceByScript(magwell, modifierScriptId);
    string ammunitionModBlock = FindSubResourceByScript(magwell, ammunitionModScriptId);
    string modifierId = FindSubResourceId(modifierBlock);

    Assert.True(modifierBlock.Contains("Operation = 0", StringComparison.Ordinal));
    Assert.True(modifierBlock.Contains("Value = 2.0", StringComparison.Ordinal));
    Assert.True(ammunitionModBlock.Contains("Modifiers =", StringComparison.Ordinal));
    Assert.True(ammunitionModBlock.Contains($"SubResource(\"{modifierId}\")", StringComparison.Ordinal));
  }

  [TestCase(TestName = "Frag grenade blast authors a damage effect")]
  public void FragGrenadeHasBlastDamage()
  {
    string grenade = ReadResource("resources/items/frag_grenade.tres");
    string blastScriptId = FindExtResourceId(grenade, "scripts/items/capabilities/BlastCapabilityData.cs");
    string damageScriptId = FindExtResourceId(grenade, "scripts/items/effects/DamageEffectData.cs");
    string blastBlock = FindSubResourceByScript(grenade, blastScriptId);
    string damageBlock = FindSubResourceByScript(grenade, damageScriptId);
    string damageId = FindSubResourceId(damageBlock);

    Assert.True(damageBlock.Contains("BaseDamage = 6", StringComparison.Ordinal));
    Assert.True(blastBlock.Contains("Effects =", StringComparison.Ordinal));
    Assert.True(blastBlock.Contains($"SubResource(\"{damageId}\")", StringComparison.Ordinal));
  }

  [TestCase(TestName = "TestCampaign declares the authored armory and mod stock")]
  public void TestCampaignDeclaresStock()
  {
    string campaign = ReadCampaign();
    string armoryScriptId = FindExtResourceId(campaign, "scripts/gamestate/ArmoryEntryData.cs");
    string modStockScriptId = FindExtResourceId(campaign, "scripts/gamestate/ModStockEntryData.cs");

    Assert.Equal(4, CountOccurrences(campaign, $"script = ExtResource(\"{armoryScriptId}\")"));
    Assert.Equal(4, CountOccurrences(campaign, $"script = ExtResource(\"{modStockScriptId}\")"));

    string[] armoryEntries =
    [
      FindStockEntry(campaign, armoryScriptId, "Item", "resources/weapons/service_pistol.tres", 2),
      FindStockEntry(campaign, armoryScriptId, "Item", "resources/weapons/combat_rifle.tres", 1),
      FindStockEntry(campaign, armoryScriptId, "Item", "resources/items/vest.tres", 2),
      FindStockEntry(campaign, armoryScriptId, "Item", "resources/items/frag_grenade.tres", 4)
    ];
    string[] modStockEntries =
    [
      FindStockEntry(campaign, modStockScriptId, "Mod", "resources/mods/reflex_chip.tres", 1),
      FindStockEntry(campaign, modStockScriptId, "Mod", "resources/mods/cardio_stims.tres", 2),
      FindStockEntry(campaign, modStockScriptId, "Mod", "resources/mods/long_barrel.tres", -1),
      FindStockEntry(campaign, modStockScriptId, "Mod", "resources/mods/extended_magwell.tres", 1)
    ];

    AssertStockArray(campaign, "Armory", armoryEntries);
    AssertStockArray(campaign, "ModStock", modStockEntries);
  }

  [TestCase(TestName = "Every TestCampaign external resource path exists")]
  public void TestCampaignExternalResourcesExist()
  {
    string campaign = ReadCampaign();
    const string marker = "path=\"res://";
    int searchFrom = 0;

    while (true)
    {
      int pathStart = campaign.IndexOf(marker, searchFrom, StringComparison.Ordinal);
      if (pathStart < 0)
        break;

      pathStart += marker.Length;
      int pathEnd = campaign.IndexOf('"', pathStart);
      Assert.True(pathEnd >= 0);

      string relativePath = campaign[pathStart..pathEnd];
      Assert.True(File.Exists(Path.Combine(MainRoot, relativePath)));
      searchFrom = pathEnd + 1;
    }
  }

  private static string ResolveMainRoot()
  {
    string root = Path.GetFullPath("..");
    if (!File.Exists(Path.Combine(root, "project.godot")))
      root = Path.GetFullPath(Path.Combine(root, ".."));

    Assert.True(File.Exists(Path.Combine(root, "project.godot")));
    return root;
  }

  private static string ReadCampaign() => ReadResource("resources/geoscape/TestCampaign.tres");

  private static string ReadResource(string relativePath) =>
    File.ReadAllText(Path.Combine(MainRoot, relativePath))
      .Replace("\r\n", "\n", StringComparison.Ordinal);

  private static string FindExtResourceId(string campaign, string relativePath)
  {
    string marker = $"path=\"res://{relativePath}\"";
    int pathPosition = campaign.IndexOf(marker, StringComparison.Ordinal);
    Assert.True(pathPosition >= 0);

    int lineStart = campaign.LastIndexOf("[ext_resource", pathPosition, StringComparison.Ordinal);
    int lineEnd = campaign.IndexOf('\n', pathPosition);
    string line = campaign[lineStart..lineEnd];
    const string idMarker = "id=\"";
    int idStart = line.LastIndexOf(idMarker, StringComparison.Ordinal);
    Assert.True(idStart >= 0);
    idStart += idMarker.Length;
    int idEnd = line.IndexOf('"', idStart);
    return line[idStart..idEnd];
  }

  private static string FindSubResourceByScript(string content, string scriptId)
  {
    string scriptReference = $"script = ExtResource(\"{scriptId}\")";
    int scriptPosition = content.IndexOf(scriptReference, StringComparison.Ordinal);
    Assert.True(scriptPosition >= 0);

    int blockStart = content.LastIndexOf("[sub_resource", scriptPosition, StringComparison.Ordinal);
    Assert.True(blockStart >= 0);
    int blockEnd = content.IndexOf("\n\n", scriptPosition, StringComparison.Ordinal);
    return content[blockStart..(blockEnd >= 0 ? blockEnd : content.Length)];
  }

  private static string FindSubResourceId(string block)
  {
    const string idMarker = "id=\"";
    int idStart = block.IndexOf(idMarker, StringComparison.Ordinal);
    Assert.True(idStart >= 0);
    idStart += idMarker.Length;
    int idEnd = block.IndexOf('"', idStart);
    Assert.True(idEnd >= 0);
    return block[idStart..idEnd];
  }

  private static string MainResourceBlock(string content)
  {
    int resourceStart = content.LastIndexOf("[resource]", StringComparison.Ordinal);
    Assert.True(resourceStart >= 0);
    return content[resourceStart..];
  }

  private static string FindStockEntry(
    string campaign,
    string scriptId,
    string stockProperty,
    string resourcePath,
    int expectedCount)
  {
    string resourceId = FindExtResourceId(campaign, resourcePath);
    string reference = $"{stockProperty} = ExtResource(\"{resourceId}\")";
    int referencePosition = campaign.IndexOf(reference, StringComparison.Ordinal);
    Assert.True(referencePosition >= 0);

    int blockStart = campaign.LastIndexOf("[sub_resource", referencePosition, StringComparison.Ordinal);
    int blockEnd = campaign.IndexOf("\n\n", referencePosition, StringComparison.Ordinal);
    string block = campaign[blockStart..blockEnd];
    Assert.True(block.Contains($"script = ExtResource(\"{scriptId}\")", StringComparison.Ordinal));
    Assert.True(block.Contains($"Count = {expectedCount}", StringComparison.Ordinal));

    const string idMarker = "id=\"";
    int idStart = block.IndexOf(idMarker, StringComparison.Ordinal) + idMarker.Length;
    int idEnd = block.IndexOf('"', idStart);
    return block[idStart..idEnd];
  }

  private static void AssertStockArray(string campaign, string property, string[] expectedEntries)
  {
    int resourceStart = campaign.IndexOf("[resource]\n", StringComparison.Ordinal);
    Assert.True(resourceStart >= 0);
    int lineStart = campaign.IndexOf($"{property} = [", resourceStart, StringComparison.Ordinal);
    Assert.True(lineStart >= 0);
    int lineEnd = campaign.IndexOf('\n', lineStart);
    string line = campaign[lineStart..lineEnd];

    Assert.Equal(4, CountOccurrences(line, "SubResource("));
    foreach (string entry in expectedEntries)
      Assert.True(line.Contains($"SubResource(\"{entry}\")", StringComparison.Ordinal));
  }

  private static int CountOccurrences(string content, string value)
  {
    int count = 0;
    int searchFrom = 0;
    while ((searchFrom = content.IndexOf(value, searchFrom, StringComparison.Ordinal)) >= 0)
    {
      count++;
      searchFrom += value.Length;
    }

    return count;
  }
}
