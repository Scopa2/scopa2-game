using System;
using System.Collections.Generic;
using Godot;
using Scopa2Game.Scripts.Models;

namespace Scopa2Game.Scripts;

/// <summary>
/// Horizontal panel showing a player's owned santi.
/// Opponent's panel goes top-left, player's goes bottom-left.
/// </summary>
public partial class SantiPanel : PanelContainer
{
    private const string CardBackPath = "res://assets/textures/deck/scopaback.png";

    public event Action<ShopItem> SantoClicked;

    private readonly HBoxContainer _slotsContainer = new();
    private readonly List<SantiSlot> _slots = new();
    private string _headerText;

    public SantiPanel(string headerText)
    {
        _headerText = headerText;
    }

    public override void _Ready()
    {
        // Semi-transparent dark panel with gold border
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.1f, 0.07f, 0.75f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(0.55f, 0.35f, 0.12f, 0.5f),
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };
        AddThemeStyleboxOverride("panel", style);

        var mainHbox = new HBoxContainer();
        mainHbox.AddThemeConstantOverride("separation", 4);
        AddChild(mainHbox);

        // Header label
        var header = new Label
        {
            Text = _headerText,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f, 0.8f));
        header.AddThemeFontSizeOverride("font_size", 10);
        mainHbox.AddChild(header);

        // Vertical separator
        var sep = new VSeparator();
        sep.AddThemeStyleboxOverride("separator", new StyleBoxFlat
        {
            BgColor = new Color(0.55f, 0.35f, 0.12f, 0.3f),
            ContentMarginLeft = 1,
            ContentMarginRight = 1
        });
        mainHbox.AddChild(sep);

        _slotsContainer.AddThemeConstantOverride("separation", 4);
        mainHbox.AddChild(_slotsContainer);
    }

    /// <summary>
    /// Update the displayed santi from game state.
    /// </summary>
    public void SyncSanti(List<ShopItem> items)
    {
        items ??= new List<ShopItem>();

        // Remove excess slots
        while (_slots.Count > items.Count)
        {
            var slot = _slots[^1];
            _slots.RemoveAt(_slots.Count - 1);
            slot.QueueFree();
        }

        // Update existing slots
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].SetItem(items[i]);
        }

        // Add new slots
        for (int i = _slots.Count; i < items.Count; i++)
        {
            var slot = new SantiSlot();
            slot.Clicked += OnSlotClicked;
            _slots.Add(slot);
            _slotsContainer.AddChild(slot);
            slot.SetItem(items[i]);
        }

        // Hide panel if no santi
        Visible = items.Count > 0;
    }

    private void OnSlotClicked(ShopItem item)
    {
        if (item != null)
            SantoClicked?.Invoke(item);
    }

    /// <summary>
    /// Individual santo slot — small card thumbnail.
    /// </summary>
    private partial class SantiSlot : VBoxContainer
    {
        public event Action<ShopItem> Clicked;

        private ShopItem _item;
        private TextureButton _cardButton;
        private Label _nameLabel;

        public override void _Ready()
        {
            AddThemeConstantOverride("separation", 1);

            _cardButton = new TextureButton
            {
                CustomMinimumSize = new Vector2(36, 48),
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                IgnoreTextureSize = true
            };
            _cardButton.Pressed += () => Clicked?.Invoke(_item);
            AddChild(_cardButton);

            _nameLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(36, 0)
            };
            _nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.65f, 0.8f));
            _nameLabel.AddThemeFontSizeOverride("font_size", 7);
            AddChild(_nameLabel);
        }

        public void SetItem(ShopItem item)
        {
            _item = item;
            _cardButton.TextureNormal = GD.Load<Texture2D>(CardBackPath);
            _cardButton.Modulate = new Color(0.7f, 0.9f, 1f); // light blue tint for owned santi
            _nameLabel.Text = item.Name?.Length > 6 ? item.Name[..6] + "…" : item.Name ?? "?";
        }
    }
}
