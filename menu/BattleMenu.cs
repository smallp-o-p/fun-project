using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Godot;

public partial class BattleMenu : GridContainer
{
  private int currentRow;
  private int currentCol;

  [Export] public int GridColumns { get; set; } = 1;

  private List<Button> menuItems;
  private int lastFocusedIndex = 0;
  private Viewport currentViewport = null;

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    Columns = GridColumns;
    currentRow = 0; currentCol = 0;
    menuItems = [];

    foreach (Node child in GetChildren())
    {
      if (child is Button btn)
      {
        menuItems.Add(btn);
        btn.FocusEntered += () => OnButtonFocusEntered(btn);
      }
    }
    SetProcessInput(true);
  }

  private void OnButtonFocusEntered(Button btn)
  {
    lastFocusedIndex = menuItems.IndexOf(btn);
  }

  public void RestoreFocus()
  {
    // Call this when the menu comes back into focus
    if (menuItems.Count > 0 && lastFocusedIndex >= 0 && lastFocusedIndex < menuItems.Count)
    {
      menuItems[lastFocusedIndex].GrabFocus();
    }
    else if (menuItems.Count > 0)
    {
      menuItems[0].GrabFocus();
    }
  }
}
