using System;
using System.Collections.Generic;
using Godot;
using Scopa2Game.Scripts.Models;

namespace Scopa2Game.Scripts;

/// <summary>
/// A custom, self-contained UI control representing the active games rejoin list.
/// Handles programmatically setting up layouts, dynamic row rendering, turn status colors, and rejoining events.
/// </summary>
public partial class ActiveGamesPanel : Panel
{
    public event Action<string> RejoinPressed;

    private VBoxContainer _activeGamesContainer;

    /// <summary>
    /// Programmatically sets up child containers, margins, headers, and scroll areas.
    /// Reuses styling parameters from existing menu structures.
    /// </summary>
    public void Setup(StyleBox panelStyle, StyleBox separatorStyle)
    {
        Name = "ActiveGamesPanel";
        Visible = false;
        
        if (panelStyle != null)
        {
            AddThemeStyleboxOverride("panel", panelStyle);
        }

        // Position it to the right of the menu panel
        SetAnchorsPreset(LayoutPreset.Center);
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = 190f;
        OffsetTop = -190f;
        OffsetRight = 470f;
        OffsetBottom = 190f;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;

        // Add a VBoxContainer inside the panel for structure
        var mainLayout = new VBoxContainer();
        mainLayout.SetAnchorsPreset(LayoutPreset.FullRect);
        mainLayout.AnchorLeft = 0.0f;
        mainLayout.AnchorTop = 0.0f;
        mainLayout.AnchorRight = 1.0f;
        mainLayout.AnchorBottom = 1.0f;
        mainLayout.OffsetLeft = 20f;
        mainLayout.OffsetTop = 20f;
        mainLayout.OffsetRight = -20f;
        mainLayout.OffsetBottom = -20f;
        mainLayout.AddThemeConstantOverride("separation", 10);
        AddChild(mainLayout);

        // Title Label
        var titleLabel = new Label();
        titleLabel.Text = "ACTIVE GAMES";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f)); // Golden color
        titleLabel.AddThemeFontSizeOverride("font_size", 18);
        mainLayout.AddChild(titleLabel);

        // HSeparator
        var separator = new HSeparator();
        if (separatorStyle != null)
        {
            separator.AddThemeStyleboxOverride("separator", separatorStyle);
        }
        mainLayout.AddChild(separator);

        // Scroll Container
        var scrollContainer = new ScrollContainer();
        scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        mainLayout.AddChild(scrollContainer);

        // The container that will actually hold our active game rows
        _activeGamesContainer = new VBoxContainer();
        _activeGamesContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _activeGamesContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _activeGamesContainer.AddThemeConstantOverride("separation", 8);
        scrollContainer.AddChild(_activeGamesContainer);
    }

    /// <summary>
    /// Dynamically renders/updates active game buttons inside the scroll container.
    /// Displays turn states cleanly using appropriate color-coding.
    /// </summary>
    public void Refresh(List<ActiveGame> activeGames, StyleBox btnNormal, StyleBox btnHover, StyleBox btnPressed)
    {
        // Clean up previous rows
        foreach (var child in _activeGamesContainer.GetChildren())
        {
            child.QueueFree();
        }

        if (activeGames == null || activeGames.Count == 0)
        {
            Hide();
            return;
        }

        Show();

        foreach (var game in activeGames)
        {
            // Create a gorgeous container row for each game
            var rowButton = new Button();
            rowButton.CustomMinimumSize = new Vector2(0, 50);
            rowButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            
            // Set styles matching the game button styles
            if (btnNormal != null) rowButton.AddThemeStyleboxOverride("normal", btnNormal);
            if (btnHover != null) rowButton.AddThemeStyleboxOverride("hover", btnHover);
            if (btnPressed != null) rowButton.AddThemeStyleboxOverride("pressed", btnPressed);

            // Add text and metadata layout
            var vbox = new VBoxContainer();
            vbox.SetAnchorsPreset(LayoutPreset.FullRect);
            vbox.AnchorLeft = 0.0f;
            vbox.AnchorTop = 0.0f;
            vbox.AnchorRight = 1.0f;
            vbox.AnchorBottom = 1.0f;
            vbox.OffsetLeft = 8f;
            vbox.OffsetTop = 4f;
            vbox.OffsetRight = -8f;
            vbox.OffsetBottom = -4f;
            vbox.Alignment = VBoxContainer.AlignmentMode.Center;
            vbox.MouseFilter = Control.MouseFilterEnum.Ignore; // Allow button to capture clicks
            rowButton.AddChild(vbox);

            var gameTitle = new Label();
            string oppName = string.IsNullOrEmpty(game.OpponentName) ? $"Player {game.OpponentId}" : game.OpponentName;
            gameTitle.Text = $"vs {oppName}";
            gameTitle.AddThemeFontSizeOverride("font_size", 12);
            gameTitle.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.85f));
            vbox.AddChild(gameTitle);

            var turnStatus = new Label();
            if (game.IsMyTurn)
            {
                turnStatus.Text = "★ YOUR TURN ★";
                turnStatus.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 0.4f)); // Rich Green
            }
            else
            {
                turnStatus.Text = "Opponent's Turn";
                turnStatus.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f)); // Muted Silver
            }
            turnStatus.AddThemeFontSizeOverride("font_size", 10);
            vbox.AddChild(turnStatus);

            // Hook up press action
            string currentId = game.Id;
            rowButton.Pressed += () => RejoinPressed?.Invoke(currentId);
            
            _activeGamesContainer.AddChild(rowButton);
        }
    }
}
