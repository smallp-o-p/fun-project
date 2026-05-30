using FunProject.Core;
using FunProject.Stats;
using System.Collections.Generic;

namespace FunProject.Combatants;


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
  public Dictionary<Faction, Stat> FriendlinessToOthers { get; set; } = [];

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
