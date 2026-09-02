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
    string rifle = File.ReadAllText(Path.Combine(MainRoot, "resources/weapons/combat_rifle.tres"))
      .Replace("\r\n", "\n", StringComparison.Ordinal);
    string pistol = File.ReadAllText(Path.Combine(MainRoot, "resources/weapons/service_pistol.tres"))
      .Replace("\r\n", "\n", StringComparison.Ordinal);

    Assert.True(rifle.Contains(
      "path=\"res://scripts/items/capabilities/ModSlotsCapabilityData.cs\" id=\"2_modslots\"",
      StringComparison.Ordinal));
    Assert.True(rifle.Contains(
      "[sub_resource type=\"Resource\" id=\"Resource_modslots\"]\nscript = ExtResource(\"2_modslots\")\nSlotCount = 2",
      StringComparison.Ordinal));
    Assert.True(rifle.Contains(
      "Capabilities = Array[ExtResource(\"2_base\")]([SubResource(\"Resource_modslots\")])",
      StringComparison.Ordinal));
    Assert.False(pistol.Contains("ModSlotsCapabilityData.cs", StringComparison.Ordinal));
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

  private static string ReadCampaign() =>
    File.ReadAllText(Path.Combine(MainRoot, "resources/geoscape/TestCampaign.tres"))
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
    int idStart = line.IndexOf(idMarker, StringComparison.Ordinal);
    Assert.True(idStart >= 0);
    idStart += idMarker.Length;
    int idEnd = line.IndexOf('"', idStart);
    return line[idStart..idEnd];
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
