using Godot;
using System;
using System.Collections.Generic;

public partial class MenuButton : Button
{
  [Export] public Texture2D RightIcon { get; set; }
  [Export] public int IconSize { get; set; } = 24;
  [Export] public int Spacing { get; set; } = 8;
  [Export] public Godot.Collections.Array<Node> nodes {get; set;}
  [Signal] public delegate void AssociatedButtonSelectedEventHandler();

  private bool _isFocused = false;

  private bool wasDisabled = false;
  public override Vector2 _GetMinimumSize()
  {
    var font = GetThemeFont("font");
    var fontSize = GetThemeFontSize("font_size");
    var buttonText = Text; // Use the base Button's Text property
    var textSize = font.GetStringSize(buttonText, HorizontalAlignment.Left, -1, fontSize);
    var leftIcon = Icon; // Use the base Button's Icon property

    // Calculate total width needed (always reserve space for right icon to prevent movement)
    float totalWidth = textSize.X;
    if (leftIcon != null)
      totalWidth += IconSize + Spacing;
    if (RightIcon != null)
      totalWidth += Spacing + IconSize;
    var maxHeight = Mathf.Max(textSize.Y, IconSize);

    return new Vector2(totalWidth, maxHeight);
  }

  public override void _Ready()
  {
    // Make button transparent
    AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
    AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
    AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
    AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

    // Hide the base text rendering by making font color transparent
    AddThemeColorOverride("font_color", new Color(0, 0, 0, 0));
    AddThemeColorOverride("font_hover_color", new Color(0, 0, 0, 0));
    AddThemeColorOverride("font_pressed_color", new Color(0, 0, 0, 0));
    AddThemeColorOverride("font_focus_color", new Color(0, 0, 0, 0));
    AddThemeColorOverride("font_disabled_color", new Color(0, 0, 0, 0));

    // Hide the base icon rendering by making it fully transparent
    AddThemeColorOverride("icon_normal_color", new Color(1, 1, 1, 0));
    AddThemeColorOverride("icon_hover_color", new Color(1, 1, 1, 0));
    AddThemeColorOverride("icon_pressed_color", new Color(1, 1, 1, 0));
    AddThemeColorOverride("icon_focus_color", new Color(1, 1, 1, 0));
    AddThemeColorOverride("icon_disabled_color", new Color(1, 1, 1, 0));

    // Connect focus signals
    FocusEntered += OnFocusEntered;
    FocusExited += OnFocusExited;
    Pressed += OnPressed;

    // Enable custom drawing
    QueueRedraw();
  }

  private void OnPressed()
  {
    GD.Print("Pressed button!");
  }

  private void OnFocusEntered()
  {
    _isFocused = true;

    if(nodes != null)
    {
      foreach (Node n in nodes)
      {
        EmitSignal(SignalName.AssociatedButtonSelected, n);
      }
    }

    QueueRedraw();
  }

  private void OnFocusExited()
  {
    _isFocused = false;
    QueueRedraw();
  }

  public override void _Draw()
  {
    var font = GetThemeFont("font");
    var fontSize = GetThemeFontSize("font_size");
    var buttonText = Text; // Use the base Button's Text property
    var textSize = font.GetStringSize(buttonText, HorizontalAlignment.Left, -1, fontSize);
    var leftIcon = Icon; // Use the base Button's Icon property

    // Calculate total width needed (always reserve space for right icon to prevent movement)
    // Starting X position (centered)
    float currentX = 0;
    float centerY = Size.Y / 2;

    Color textColor = Disabled ? new Color(1, 1, 1, 0.4f) : new Color(1, 1, 1, 1);
    Color iconModulate = Disabled ? new Color(1, 1, 1, 0.4f) : new Color(1, 1, 1, 1);

    // Draw left icon
    if (leftIcon != null)
    {
      var iconRect = new Rect2(currentX, centerY - IconSize / 2, IconSize, IconSize);
      DrawTextureRect(leftIcon, iconRect, false);
      currentX += IconSize + Spacing;
    }

    // Draw text
    var textPos = new Vector2(currentX, centerY + textSize.Y / 2 - font.GetDescent(fontSize));
    DrawString(font, textPos, buttonText, HorizontalAlignment.Left, -1, fontSize, textColor);
    currentX += textSize.X;

    // Draw right icon (only when focused)
    if (RightIcon != null && _isFocused)
    {
      currentX += Spacing;
      var iconRect = new Rect2(currentX, centerY - IconSize / 2, IconSize, IconSize);
      DrawTextureRect(RightIcon, iconRect, false, iconModulate);
    }
  }


  public override void _Process(double delta)
  {
    // Redraw if focus state might have changed or text changed
    bool needsRedraw = false;
    if (HasFocus() != _isFocused)
    {
      _isFocused = HasFocus();
      needsRedraw = true;
    }

    if(Disabled != wasDisabled)
    {
      wasDisabled = Disabled;
      needsRedraw = true;
    }

    if(needsRedraw)
      QueueRedraw();
  }

}
