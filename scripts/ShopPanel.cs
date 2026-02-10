using System;
using System.Collections.Generic;
using Godot;
using Scopa2Game.Scripts.Models;

namespace Scopa2Game.Scripts;

/// <summary>
/// Always-visible shop panel showing up to 3 santo cards.
/// Built entirely in code — no .tscn needed.
/// </summary>
public partial class ShopPanel : PanelContainer
{
    private const int MaxSlots = 3;
    private const string CardBackPath = "res://assets/textures/deck/scopaback.png";

    public event Action<ShopItem> SantoClicked;

    private readonly VBoxContainer _slotsContainer = new();
    private readonly List<ShopSlot> _slots = new();

    public override void _Ready()
    {
        // Panel styling — dark with gold border, matching existing UI
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.1f, 0.07f, 0.85f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            BorderColor = new Color(0.55f, 0.35f, 0.12f, 0.7f),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
        AddThemeStyleboxOverride("panel", style);

        var mainVbox = new VBoxContainer();
        mainVbox.AddThemeConstantOverride("separation", 6);
        AddChild(mainVbox);

        // Header
        var header = new Label
        {
            Text = "\u2726 SHOP \u2726",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        header.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f, 0.95f));
        header.AddThemeFontSizeOverride("font_size", 13);
        header.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        mainVbox.AddChild(header);

        // Separator
        var sep = new HSeparator();
        sep.AddThemeStyleboxOverride("separator", new StyleBoxFlat
        {
            BgColor = new Color(0.55f, 0.35f, 0.12f, 0.4f),
            ContentMarginTop = 1,
            ContentMarginBottom = 1
        });
        mainVbox.AddChild(sep);

        _slotsContainer.AddThemeConstantOverride("separation", 6);
        mainVbox.AddChild(_slotsContainer);

        // Create initial empty slots
        for (int i = 0; i < MaxSlots; i++)
        {
            var slot = new ShopSlot();
            slot.Clicked += OnSlotClicked;
            _slots.Add(slot);
            _slotsContainer.AddChild(slot);
            slot.SetEmpty();
        }
    }

    /// <summary>
    /// Update displayed shop items from game state.
    /// </summary>
    public void SyncShop(List<ShopItem> items)
    {
        items ??= new List<ShopItem>();

        for (int i = 0; i < MaxSlots; i++)
        {
            if (i < items.Count)
                _slots[i].SetItem(items[i]);
            else
                _slots[i].SetEmpty();
        }
    }

    private void OnSlotClicked(ShopItem item)
    {
        if (item != null)
            SantoClicked?.Invoke(item);
    }

    /// <summary>
    /// Individual card slot inside the shop.
    /// </summary>
    private partial class ShopSlot : VBoxContainer
    {
        public event Action<ShopItem> Clicked;

        private ShopItem _item;
        private TextureButton _cardButton;
        private Label _nameLabel;

        public override void _Ready()
        {
            AddThemeConstantOverride("separation", 2);

            // Card button (shows card back as placeholder, rotated -90° to face the table)
            _cardButton = new TextureButton
            {
                CustomMinimumSize = new Vector2(60, 80),
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                IgnoreTextureSize = true,
                PivotOffset = new Vector2(30, 40)
            };
            _cardButton.Rotation = Mathf.DegToRad(-90);
            _cardButton.Pressed += () => Clicked?.Invoke(_item);
            AddChild(_cardButton);

            // Name label (also rotated -90° to match card orientation)
            _nameLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(60, 0)
            };
            _nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.65f, 0.9f));
            _nameLabel.AddThemeFontSizeOverride("font_size", 9);
            _nameLabel.PivotOffset = new Vector2(30, 6);
            _nameLabel.Rotation = Mathf.DegToRad(-90);
            AddChild(_nameLabel);
        }

        public void SetItem(ShopItem item)
        {
            _item = item;
            Visible = true;

            // Use card back as placeholder sprite
            _cardButton.TextureNormal = GD.Load<Texture2D>(CardBackPath);
            _cardButton.Modulate = new Color(0.85f, 0.75f, 1f); // slight purple tint to distinguish from normal cards

            _nameLabel.Text = item.Name?.Length > 10 ? item.Name[..10] + "…" : item.Name ?? "???";
        }

        public void SetEmpty()
        {
            _item = null;
            Visible = false;
        }
    }
}
