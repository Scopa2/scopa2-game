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
    private GpuParticles2D _mutationParticles;

    public CardData CardData { get; private set; }
    
    /// <summary>
    /// The original card code (immutable, used for registry and animations)
    /// </summary>
    public string OriginalCode { get; private set; }
    
    /// <summary>
    /// The effective card data after mutation (used for display and game logic)
    /// </summary>
    public CardData EffectiveCardData { get; private set; }
    
    /// <summary>
    /// Whether this card is currently mutated
    /// </summary>
    public bool IsMutated => OriginalCode != null && EffectiveCardData?.ToString() != OriginalCode;
    
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
        OriginalCode = pCardData?.ToString();
        EffectiveCardData = pCardData;
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

    /// <summary>
    /// Apply a mutation to this card, optionally with animation
    /// </summary>
    public void ApplyMutation(string newCode, bool animate = true)
    {
        if (string.IsNullOrEmpty(newCode) || newCode == "X")
        {
            ClearMutation();
            return;
        }
        
        var newCardData = new CardData(newCode);
        string previousEffectiveCode = EffectiveCardData?.ToString();
        
        EffectiveCardData = newCardData;
        
        // Only animate if this is a new mutation or the mutation changed
        if (animate && previousEffectiveCode != newCode)
        {
            PlayMutationAnimation();
        }
        
        UpdateDisplay();
    }
    
    /// <summary>
    /// Clear any mutation, restoring the original card
    /// </summary>
    public void ClearMutation()
    {
        if (CardData != null)
        {
            EffectiveCardData = CardData;
            UpdateDisplay();
        }
    }
    
    private void PlayMutationAnimation()
    {
        // Create sparkle/transformation effect
        CreateMutationParticles();
        
        // Scale pulse animation
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector2(1.15f, 1.15f), 0.15f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "scale", Vector2.One, 0.2f)
            .SetTrans(Tween.TransitionType.Elastic)
            .SetEase(Tween.EaseType.Out);
        
        // Golden glow flash
        var glowTween = CreateTween();
        glowTween.TweenProperty(this, "modulate", new Color(1.5f, 1.3f, 0.8f), 0.1f);
        glowTween.TweenProperty(this, "modulate", Selected ? Colors.White : new Color(0.9f, 0.9f, 0.9f), 0.3f);
    }
    
    private void CreateMutationParticles()
    {
        // Clean up existing particles
        _mutationParticles?.QueueFree();
        
        _mutationParticles = new GpuParticles2D();
        AddChild(_mutationParticles);
        _mutationParticles.Position = Size / 2;
        _mutationParticles.ZIndex = 10;
        _mutationParticles.Emitting = true;
        _mutationParticles.OneShot = true;
        _mutationParticles.Explosiveness = 0.8f;
        _mutationParticles.Amount = 20;
        _mutationParticles.Lifetime = 0.6f;
        
        // Create particle material
        var material = new ParticleProcessMaterial();
        material.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        material.EmissionSphereRadius = 30f;
        material.Direction = new Vector3(0, -1, 0);
        material.Spread = 180f;
        material.InitialVelocityMin = 50f;
        material.InitialVelocityMax = 100f;
        material.Gravity = new Vector3(0, 50, 0);
        material.ScaleMin = 2f;
        material.ScaleMax = 5f;
        
        // Golden sparkle color
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1f, 0.9f, 0.3f, 1f));
        gradient.SetColor(1, new Color(1f, 0.6f, 0.2f, 0f));
        var gradientTex = new GradientTexture1D();
        gradientTex.Gradient = gradient;
        material.ColorRamp = gradientTex;
        
        _mutationParticles.ProcessMaterial = material;
        
        // Auto-cleanup after particles finish
        GetTree().CreateTimer(1.0f).Timeout += () => 
        {
            if (IsInstanceValid(_mutationParticles))
            {
                _mutationParticles.QueueFree();
                _mutationParticles = null;
            }
        };
    }

    private void UpdateDisplay()
    {
        if (!IsInstanceValid(EffectiveCardData))
        {
            Visible = false;
            return;
        }

        Visible = true;
        Modulate = new Color(0.9f, 0.9f, 0.9f);

        if (EffectiveCardData.ToString() == "X")
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

        // Use EffectiveCardData for display (mutated value)
        var displayData = EffectiveCardData ?? CardData;
        if (displayData == null) return;

        var baseTexture = GD.Load<Texture2D>(CardDeckPath);
        int rankIndex = displayData.Rank - 1;

        if (!SuitMap.ContainsKey(displayData.Suit) || rankIndex < 0)
        {
            ShowCardBack();
            return;
        }

        int suitIndex = SuitMap[displayData.Suit];
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
