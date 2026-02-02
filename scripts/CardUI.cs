using Godot;
using Scopa2Game.Scripts.Models;
using System.Collections.Generic;

namespace Scopa2Game.Scripts;

public partial class CardUI : TextureButton
{
    private const string CardDeckPath = "res://assets/textures/deck/scopadeck.png";
    private const string CardBackPath = "res://assets/textures/deck/scopaback.png";

    private const int CardAtlasWidth = 142;
    private const int CardAtlasHeight = 190;
    
    private static readonly Dictionary<string, int> SuitMap = new()
    {
        { "C", 0 }, { "B", 1 }, { "D", 2 }, { "S", 3 }
    };

    [Signal]
    public delegate void CardUiClickedEventHandler(CardUI cardUi);

    private TextureRect _cardSprite;
    private Panel _selectionHighlight;

    public CardData CardData { get; private set; }
    public bool Selected { get; private set; } = false;

    public override void _Ready()
    {
        _cardSprite = GetNodeOrNull<TextureRect>("CardSprite");
        _selectionHighlight = GetNodeOrNull<Panel>("SelectionHighlight");

        Pressed += OnPressed;
        CustomMinimumSize = new Vector2(CardAtlasWidth / 1.5f, CardAtlasHeight / 1.5f);
        
        if (IsInstanceValid(_cardSprite))
        {
            _cardSprite.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
    }

    public async void Setup(CardData pCardData)
    {
        CardData = pCardData;
        Disabled = false;
        
        if (!IsInsideTree())
        {
            await ToSignal(this, Node.SignalName.Ready);
        }
        
        UpdateDisplay();
        Name = IsInstanceValid(CardData) ? CardData.ToString() : "Card";
    }

    public void SetDisabledState(bool isDisabled)
    {
        Disabled = isDisabled;
        if (isDisabled)
        {
            Modulate = new Color(0.5f, 0.5f, 0.5f);
            if (Selected) ToggleSelection(); // Deselect if disabling
        }
        else
        {
            Modulate = Selected ? Colors.White : new Color(0.9f, 0.9f, 0.9f);
        }
    }

    public void ToggleSelection()
    {
        Selected = !Selected;
        var tween = CreateTween();
        
        if (Selected)
        {
            // Use a springy-looking transition
            tween.TweenProperty(this, "position:y", Position.Y - 20, 0.2f)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
            Modulate = Colors.White;
        }
        else
        {
            tween.TweenProperty(this, "position:y", Position.Y + 20, 0.2f)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.In);
            Modulate = new Color(0.9f, 0.9f, 0.9f);
        }
    }

    public void SetSelectedState(bool isSelected)
    {
        if (Selected != isSelected)
        {
            ToggleSelection();
        }
    }

    public void AnimateMove(Vector2 targetPos, float delay = 0.0f)
    {
        var tween = CreateTween();
        tween.SetParallel(true);
        // Set a delay if needed for staggered animations
        tween.TweenInterval(delay);
        // Animate the movement
        tween.TweenProperty(this, "global_position", targetPos, 0.35f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }

    private void UpdateDisplay()
    {
        if (!IsInstanceValid(CardData))
        {
            Visible = false;
            return;
        }

        Visible = true;
        Modulate = new Color(0.9f, 0.9f, 0.9f);

        if (CardData.ToString() == "X")
        {
            ShowCardBack();
        }
        else
        {
            ShowCardFront();
        }
    }

    public void ShowCardBack()
    {
        if (IsInstanceValid(_cardSprite))
        {
            _cardSprite.Texture = GD.Load<Texture2D>(CardBackPath);
        }
    }

    public void ShowCardFront()
    {
        if (!IsInstanceValid(_cardSprite)) return;

        var baseTexture = GD.Load<Texture2D>(CardDeckPath);
        int rankIndex = CardData.Rank - 1;

        if (!SuitMap.ContainsKey(CardData.Suit) || rankIndex < 0)
        {
            // This can happen for the "BACK" card, it's fine.
            ShowCardBack();
            return;
        }

        int suitIndex = SuitMap[CardData.Suit];
        var atlas = new AtlasTexture();
        atlas.Atlas = baseTexture;
        atlas.Region = new Rect2(rankIndex * CardAtlasWidth, suitIndex * CardAtlasHeight, CardAtlasWidth, CardAtlasHeight);
        _cardSprite.Texture = atlas;
    }

    private void OnPressed()
    {
        EmitSignal(SignalName.CardUiClicked, this);
    }
}
