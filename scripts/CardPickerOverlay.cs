using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Scopa2Game.Scripts.Models;

namespace Scopa2Game.Scripts;

/// <summary>
/// Full-screen overlay that lets the player pick captured cards to pay for a santo.
/// Shows a paginated fan of cards (10 per page) with left/right arrows.
/// Clicking a card toggles selection (raises it). A counter shows selected/required.
/// When enough cards are selected the BUY button enables.
/// </summary>
public partial class CardPickerOverlay : CanvasLayer
{
    private const int CardsPerPage = 10;
    private const string CardDeckPath = "res://assets/textures/deck/scopadeck.png";
    private const int CardW = 95;
    private const int CardH = 127;
    private const float FanSpacing = 80f;
    private const float FanArcDeg = 3f;   // degrees between cards in the fan
    private const float SelectedRaise = 28f;

    /// <summary>Fired with (santoId, list of ORIGINAL card codes used as payment).</summary>
    public event Action<string, List<string>> PurchaseConfirmed;
    public event Action Cancelled;

    private ShopItem _item;
    private List<PickerCard> _allCards;   // all captured cards with original + effective codes
    private int _pageIndex;
    private readonly List<PickerCard> _selectedCards = new();
    private Dictionary<string, string> _mutations = new();

    // UI references (built in code)
    private Control _fanContainer;
    private Button _leftArrow;
    private Button _rightArrow;
    private Label _counterLabel;
    private Label _titleLabel;
    private Button _buyButton;
    private Button _cancelButton;
    private readonly List<FanCard> _currentFanCards = new();

    // Atlas helpers (same logic as CardUI)
    private static readonly Dictionary<string, int> SuitMap = new()
    {
        { "C", 0 }, { "B", 1 }, { "D", 2 }, { "S", 3 }
    };

    /// <summary>Call before adding to the tree.</summary>
    public void Populate(ShopItem item, List<string> capturedCardCodes, Dictionary<string, string> mutations)
    {
        _item = item;
        _mutations = mutations ?? new Dictionary<string, string>();
        // Build list with original + effective (mutated) codes
        _allCards = (capturedCardCodes ?? new List<string>())
            .Where(c => c != "X" && !string.IsNullOrEmpty(c))
            .Select(orig =>
            {
                string effective = _mutations.TryGetValue(orig, out var m) ? m : orig;
                return new PickerCard { OriginalCode = orig, EffectiveCode = effective };
            })
            .ToList();
        _pageIndex = 0;
    }

    public override void _Ready()
    {
        Layer = 110; // above SantoDetailDialog (100)

        // ── Backdrop ──
        var backdrop = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.6f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        // ── Title ──
        _titleLabel = new Label
        {
            Text = $"Pay for {_item?.Name ?? "Santo"}  —  Cost: {_item?.Cost ?? 0}",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _titleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f, 1f));
        _titleLabel.AddThemeFontSizeOverride("font_size", 20);
        _titleLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _titleLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _titleLabel.GrowHorizontal = Control.GrowDirection.Both;
        _titleLabel.OffsetTop = 40;
        _titleLabel.OffsetBottom = 70;
        _titleLabel.OffsetLeft = -300;
        _titleLabel.OffsetRight = 300;
        backdrop.AddChild(_titleLabel);

        // ── Fan container (centred) ──
        _fanContainer = new Control
        {
            CustomMinimumSize = new Vector2(0, CardH + 60)
        };
        _fanContainer.SetAnchorsPreset(Control.LayoutPreset.Center);
        _fanContainer.GrowHorizontal = Control.GrowDirection.Both;
        _fanContainer.GrowVertical = Control.GrowDirection.Both;
        _fanContainer.OffsetLeft = -((CardsPerPage * FanSpacing) / 2f);
        _fanContainer.OffsetRight = (CardsPerPage * FanSpacing) / 2f;
        _fanContainer.OffsetTop = -(CardH / 2f);
        _fanContainer.OffsetBottom = (CardH / 2f) + 30;
        backdrop.AddChild(_fanContainer);

        // ── Left Arrow (positioned just left of the fan) ──
        _leftArrow = MakeArrowButton("◀");
        _leftArrow.Position = new Vector2(-56, (CardH / 2f) - 24);
        _leftArrow.Pressed += OnPrevPage;
        _fanContainer.AddChild(_leftArrow);

        // ── Right Arrow (positioned just right of the fan) ──
        _rightArrow = MakeArrowButton("▶");
        _rightArrow.Position = new Vector2((CardsPerPage * FanSpacing) + 8, (CardH / 2f) - 24);
        _rightArrow.Pressed += OnNextPage;
        _fanContainer.AddChild(_rightArrow);

        // ── Bottom bar (counter + buttons) ──
        var bottomBar = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center
        };
        bottomBar.AddThemeConstantOverride("separation", 24);
        bottomBar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        bottomBar.GrowHorizontal = Control.GrowDirection.Both;
        bottomBar.GrowVertical = Control.GrowDirection.Begin;
        bottomBar.OffsetBottom = -36;
        bottomBar.OffsetTop = -80;
        bottomBar.OffsetLeft = -260;
        bottomBar.OffsetRight = 260;
        backdrop.AddChild(bottomBar);

        // Cancel button
        _cancelButton = MakeActionButton("CANCEL", new Color(0.45f, 0.2f, 0.15f, 0.9f),
            new Color(0.8f, 0.35f, 0.2f, 0.8f));
        _cancelButton.Pressed += () =>
        {
            Cancelled?.Invoke();
            QueueFree();
        };
        bottomBar.AddChild(_cancelButton);

        // Counter label
        _counterLabel = new Label
        {
            Text = "0 / 0",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(120, 44)
        };
        _counterLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.75f, 1f));
        _counterLabel.AddThemeFontSizeOverride("font_size", 20);
        _counterLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        bottomBar.AddChild(_counterLabel);

        // Buy button
        _buyButton = MakeActionButton("✦  BUY", new Color(0.12f, 0.38f, 0.15f, 0.9f),
            new Color(0.25f, 0.7f, 0.3f, 0.8f));
        _buyButton.Pressed += OnBuyPressed;
        bottomBar.AddChild(_buyButton);

        // Initial render
        RenderPage();
        UpdateCounter();
    }

    // ─────────────────── Pagination ───────────────────

    private int TotalPages => _allCards.Count == 0 ? 1 : (int)Math.Ceiling((double)_allCards.Count / CardsPerPage);

    private void OnPrevPage()
    {
        if (_pageIndex > 0)
        {
            _pageIndex--;
            RenderPage();
        }
    }

    private void OnNextPage()
    {
        if (_pageIndex < TotalPages - 1)
        {
            _pageIndex++;
            RenderPage();
        }
    }

    // ─────────────────── Rendering ───────────────────

    private void RenderPage()
    {
        // Clear old fan cards
        foreach (var fc in _currentFanCards)
            fc.Root.QueueFree();
        _currentFanCards.Clear();

        // Slice
        int start = _pageIndex * CardsPerPage;
        int count = Math.Min(CardsPerPage, _allCards.Count - start);
        var pageCards = _allCards.GetRange(start, count);

        float totalWidth = (count - 1) * FanSpacing + CardW;
        float containerWidth = _fanContainer.Size.X;
        if (containerWidth <= 0) containerWidth = CardsPerPage * FanSpacing;
        float startX = (containerWidth - totalWidth) / 2f;

        float midIndex = (count - 1) / 2f;

        for (int i = 0; i < count; i++)
        {
            var pc = pageCards[i];
            bool isSelected = _selectedCards.Contains(pc);

            float x = startX + i * FanSpacing;
            float angleDeg = (i - midIndex) * FanArcDeg;
            float yOffset = Math.Abs(i - midIndex) * 4f; // slight arc dip

            var fc = CreateFanCard(pc, isSelected);
            fc.Root.Position = new Vector2(x, yOffset - (isSelected ? SelectedRaise : 0));
            fc.Root.RotationDegrees = angleDeg;
            _fanContainer.AddChild(fc.Root);
            _currentFanCards.Add(fc);
        }

        // Arrow visibility and positioning
        _leftArrow.Visible = _pageIndex > 0;
        _leftArrow.Position = new Vector2(startX - 56, (CardH / 2f) - 24);

        _rightArrow.Visible = _pageIndex < TotalPages - 1;
        float lastCardRight = startX + (count - 1) * FanSpacing + CardW;
        _rightArrow.Position = new Vector2(lastCardRight + 8, (CardH / 2f) - 24);
    }

    private FanCard CreateFanCard(PickerCard pc, bool selected)
    {
        var btn = new TextureButton
        {
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(CardW, CardH),
            Size = new Vector2(CardW, CardH),
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            PivotOffset = new Vector2(CardW / 2f, CardH)
        };

        // Display using the effective (mutated) card
        var effectiveData = new CardData(pc.EffectiveCode);
        btn.TextureNormal = GetCardAtlasTexture(effectiveData);

        // Dim unselected cards slightly
        btn.Modulate = selected ? Colors.White : new Color(0.75f, 0.75f, 0.75f);

        var fc = new FanCard { Root = btn, Card = pc, IsSelected = selected };

        btn.Pressed += () => OnFanCardClicked(fc);

        return fc;
    }

    private void OnFanCardClicked(FanCard fc)
    {
        if (fc.IsSelected)
        {
            // Deselect
            fc.IsSelected = false;
            _selectedCards.Remove(fc.Card);
            // Animate down
            var tween = fc.Root.CreateTween();
            tween.TweenProperty(fc.Root, "position:y", fc.Root.Position.Y + SelectedRaise, 0.15f)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
            fc.Root.Modulate = new Color(0.75f, 0.75f, 0.75f);
        }
        else
        {
            // Select
            fc.IsSelected = true;
            _selectedCards.Add(fc.Card);
            // Animate up
            var tween = fc.Root.CreateTween();
            tween.TweenProperty(fc.Root, "position:y", fc.Root.Position.Y - SelectedRaise, 0.15f)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
            fc.Root.Modulate = Colors.White;
        }

        UpdateCounter();
    }

    // ─────────────────── Card Pricing ───────────────────

    /// <summary>
    /// Card price based on effective (mutated) rank and suit.
    /// Rank value: 1→11, 2→2, 3→3 … 10→10.
    /// Suit bonus: Bastoni(B)→+0, Spade(S)→+1, Coppe(C)→+2, Denari(D)→+3.
    /// </summary>
    private static int GetCardPrice(CardData card)
    {
        if (card == null || card.Suit == "X") return 0;
        int rankValue = card.Rank == 1 ? 11 : card.Rank;
        int suitBonus = card.Suit switch
        {
            "B" => 0,
            "S" => 1,
            "C" => 2,
            "D" => 3,
            _ => 0
        };
        return rankValue + suitBonus;
    }

    private int GetSelectedValue()
    {
        int total = 0;
        foreach (var pc in _selectedCards)
        {
            var data = new CardData(pc.EffectiveCode);
            total += GetCardPrice(data);
        }
        return total;
    }

    // ─────────────────── Counter & Buy ───────────────────

    private void UpdateCounter()
    {
        int cost = _item?.Cost ?? 0;
        int selectedValue = GetSelectedValue();

        _counterLabel.Text = $"{selectedValue} / {cost}";

        // Colour feedback
        if (selectedValue >= cost && cost > 0)
            _counterLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.45f, 1f)); // green
        else
            _counterLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.75f, 1f)); // gold

        // Enable buy when selected value meets or exceeds the cost
        bool canBuy = selectedValue >= cost && cost > 0;
        _buyButton.Disabled = !canBuy;
        _buyButton.Modulate = canBuy ? Colors.White : new Color(0.5f, 0.5f, 0.5f, 0.7f);
    }

    private void OnBuyPressed()
    {
        int cost = _item?.Cost ?? 0;
        if (GetSelectedValue() < cost) return;
        // Send ORIGINAL codes back (server tracks cards by original code)
        var originalCodes = _selectedCards.Select(pc => pc.OriginalCode).ToList();
        PurchaseConfirmed?.Invoke(_item.Id, originalCodes);
        QueueFree();
    }

    // ─────────────────── Helpers ───────────────────

    private static AtlasTexture GetCardAtlasTexture(CardData card)
    {
        const int atlasW = 142;
        const int atlasH = 190;

        if (card == null || card.Suit == "X" || !SuitMap.ContainsKey(card.Suit))
            return null;

        int col = card.Rank - 1; // 1-based rank → 0-based column
        int row = SuitMap[card.Suit];

        var atlas = new AtlasTexture
        {
            Atlas = GD.Load<Texture2D>(CardDeckPath),
            Region = new Rect2(col * atlasW, row * atlasH, atlasW, atlasH)
        };
        return atlas;
    }

    private static Button MakeArrowButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(48, 48)
        };
        btn.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.6f, 0.9f));
        btn.AddThemeFontSizeOverride("font_size", 22);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.1f, 0.08f, 0.8f),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(0.6f, 0.4f, 0.15f, 0.6f),
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };
        btn.AddThemeStyleboxOverride("normal", style);
        var hover = (StyleBoxFlat)style.Duplicate();
        hover.BgColor = new Color(0.2f, 0.16f, 0.1f, 0.9f);
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeStyleboxOverride("pressed", style);

        return btn;
    }

    private static Button MakeActionButton(string text, Color bgColor, Color borderColor)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(130, 44)
        };
        btn.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.85f, 1f));
        btn.AddThemeFontSizeOverride("font_size", 15);

        var style = new StyleBoxFlat
        {
            BgColor = bgColor,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            BorderColor = borderColor
        };
        btn.AddThemeStyleboxOverride("normal", style);
        var hover = (StyleBoxFlat)style.Duplicate();
        hover.BgColor = bgColor with { R = bgColor.R + 0.1f };
        hover.BorderColor = borderColor with { A = 1f };
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeStyleboxOverride("pressed", style);

        return btn;
    }

    /// <summary>Tiny helper to hold fan card state.</summary>
    private class FanCard
    {
        public TextureButton Root;
        public PickerCard Card;
        public bool IsSelected;
    }

    /// <summary>Tracks original and effective (mutated) code for a captured card.</summary>
    private class PickerCard
    {
        public string OriginalCode;
        public string EffectiveCode;
    }
}
