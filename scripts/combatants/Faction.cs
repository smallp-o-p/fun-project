using FunProject.Core;
using FunProject.Stats;

enum FactionStanding
{
  Friendly,
  Neutral,
  Hostile
};

public class Faction : HasNameAndDescription
{
  public string Name { get; set; } = "Faction";
  public string Description { get; set; } = "Faction Description";
  public System.Collections.Generic.Dictionary<Faction, Stat> FriendlinessToOthers { get; set; }= [];

  public string GetName()
  {
    return Name;
  }

  public string GetDescription()
  {
    return Description;
  }

  public Faction(FactionData data)
  {
    Name = data.Name;
    Description = data.Description;
  }
}
