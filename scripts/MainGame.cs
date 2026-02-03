using Godot;
using Scopa2Game.Scripts.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Scopa2Game.Scripts;

/// <summary>
/// Main game controller for Scopa card game.
/// Handles UI, game state synchronization, player input, and animations.
/// </summary>
public partial class MainGame : Control
{
    #region Constants
    
    private const string PlayerId = "p2";
    private const string CardBackTexturePath = "res://assets/textures/deck/scopaback.png";
    private const float CardWidth = 95f;
    private const float CardHeight = 127f;
    private const int DeckVisualStackSize = 6;
    
    // Animation durations (in seconds) - keeping them snappy
    private const float AnimDealCard = 0.25f;
    private const float AnimPlayCard = 0.2f;
    private const float AnimCollectCards = 0.18f;
    private const float AnimCardStagger = 0.03f;
    
    #endregion

    #region Node References
    
    private PackedScene _cardScene;
    private NetworkManager _network;
    
    // Game areas
    private Control _opponentHand;
    private Control _table;
    private Control _playerHand;
    private Control _deckPosition;
    private Control _playerCapturePile;
    private Control _opponentCapturePile;
    
    // UI elements
    private Panel _menuPanel;
    private Panel _turnIndicator;
    private Label _turnLabel;
    private Label _deckCountLabel;
    private Label _playerCaptureCount;
    private Label _opponentCaptureCount;
    private Label _waitingLabel;
    private Button _startButton;
    private Button _joinButton;
    private LineEdit _gameIdInput;
    
    // Deck visual stack
    private readonly List<TextureRect> _deckVisuals = new();
    
    #endregion

    #region Game State
    
    private readonly Dictionary<string, CardUI> _cardRegistry = new();
    private readonly List<CardUI> _playerCaptured = new();
    private readonly List<CardUI> _opponentCaptured = new();
    
    private bool _isPlayerTurn;
    private CardUI _selectedCard;
    private readonly List<CardUI> _selectedTableCards = new();
    
    #endregion

    #region Initialization
    
    public override void _Ready()
    {
        CacheNodeReferences();
        CreateDeckVisuals();
        ConnectSignals();
    }
    
    private void CacheNodeReferences()
    {
        _cardScene = GD.Load<PackedScene>("res://card.tscn");
        _network = GetNode<NetworkManager>("/root/NetworkManager");
        
        // Game areas
        _opponentHand = GetNode<Control>("GameArea/OpponentHandAnchor");
        _table = GetNode<Control>("GameArea/TableAnchor");
        _playerHand = GetNode<Control>("GameArea/MyHandAnchor");
        _deckPosition = GetNode<Control>("GameArea/DeckArea/VBoxContainer/DeckVisualContainer/DeckPosition");
        _playerCapturePile = GetNode<Control>("GameArea/PlayerCapturedArea/VBox/PlayerCapturedAnchor");
        _opponentCapturePile = GetNode<Control>("GameArea/OpponentCapturedArea/VBox/OpponentCapturedAnchor");
        
        // UI
        _menuPanel = GetNode<Panel>("UI/MenuPanel");
        _turnIndicator = GetNode<Panel>("UI/TurnIndicator");
        _turnLabel = GetNode<Label>("UI/TurnIndicator/TurnLabel");
        _deckCountLabel = GetNode<Label>("GameArea/DeckArea/VBoxContainer/DeckCountLabel");
        _playerCaptureCount = GetNode<Label>("GameArea/PlayerCapturedArea/VBox/PlayerCaptureCount");
        _opponentCaptureCount = GetNode<Label>("GameArea/OpponentCapturedArea/VBox/OpponentCaptureCount");
        _waitingLabel = GetNode<Label>("UI/WaitingLabel");
        _startButton = GetNode<Button>("UI/MenuPanel/VBoxContainer/StartButton");
        _joinButton = GetNode<Button>("UI/MenuPanel/VBoxContainer/JoinContainer/JoinGameButton");
        _gameIdInput = GetNode<LineEdit>("UI/MenuPanel/VBoxContainer/JoinContainer/JoinGameLineEdit");
    }
    
    private void CreateDeckVisuals()
    {
        var texture = GD.Load<Texture2D>(CardBackTexturePath);
        
        for (int i = 0; i < DeckVisualStackSize; i++)
        {
            var card = new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(CardWidth, CardHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(-CardWidth / 2 + i * 1.5f, -CardHeight / 2 - i * 2f),
                Rotation = Mathf.DegToRad(-2 + i * 0.8f),
                Modulate = new Color(1, 1, 1, 1f - i * 0.05f)
            };
            
            _deckPosition.AddChild(card);
            _deckVisuals.Add(card);
        }
        
        UpdateDeckDisplay(40);
    }
    
    private void ConnectSignals()
    {
        _network.StateUpdated += OnGameStateReceived;
        _startButton.Pressed += OnStartPressed;
        _joinButton.Pressed += OnJoinPressed;
    }
    
    #endregion

    #region UI Event Handlers
    
    private void OnStartPressed()
    {
        _network.StartGame();
        _menuPanel.Hide();
        _waitingLabel.Show();
    }
    
    private void OnJoinPressed()
    {
        var gameId = _gameIdInput.Text.Trim();
        if (string.IsNullOrEmpty(gameId)) return;
        
        _network.JoinGame(gameId);
        _menuPanel.Hide();
        _waitingLabel.Show();
    }
    
    #endregion

    #region Card Input Handling
    
    private void OnCardClicked(CardUI card)
    {
        if (!_isPlayerTurn) return;
        
        bool isHandCard = card.GetParent() == _playerHand;
        bool isTableCard = card.GetParent() == _table;
        
        if (isHandCard) HandleHandCardClick(card);
        else if (isTableCard) HandleTableCardClick(card);
    }
    
    private void HandleHandCardClick(CardUI card)
    {
        // Deselect if clicking same card
        if (_selectedCard == card)
        {
            DeselectAll();
            return;
        }
        
        // Select new hand card
        DeselectAll();
        _selectedCard = card;
        card.SetSelectedState(true);
        
        // Determine valid table targets
        var tableCards = GetCardsIn(_table);
        int rank = card.CardData.Rank;
        
        // Check for direct rank matches first
        var directMatches = tableCards.Where(c => c.CardData.Rank == rank).ToList();
        if (directMatches.Any())
        {
            EnableOnlyCards(directMatches);
            return;
        }
        
        // Check for sum combinations
        var validForSum = FindCardsValidForSum(rank, tableCards);
        if (validForSum.Any())
        {
            EnableOnlyCards(validForSum);
            return;
        }
        
        // No captures possible - auto throw
        ExecuteThrow(card);
    }
    
    private void HandleTableCardClick(CardUI card)
    {
        if (_selectedCard == null || card.Disabled) return;
        
        // Toggle table card selection
        if (_selectedTableCards.Contains(card))
        {
            card.SetSelectedState(false);
            _selectedTableCards.Remove(card);
        }
        else
        {
            card.SetSelectedState(true);
            _selectedTableCards.Add(card);
        }
        
        int currentSum = _selectedTableCards.Sum(c => c.CardData.Rank);
        int targetRank = _selectedCard.CardData.Rank;
        
        // Exact match - execute capture
        if (currentSum == targetRank)
        {
            ExecuteCapture(_selectedCard, _selectedTableCards.ToList());
            return;
        }
        
        // Under target - update valid next picks
        if (currentSum < targetRank)
        {
            int remaining = targetRank - currentSum;
            var availableCards = GetCardsIn(_table).Where(c => !_selectedTableCards.Contains(c)).ToList();
            var validNext = FindCardsValidForSum(remaining, availableCards);
            
            EnableOnlyCards(_selectedTableCards.Concat(validNext).ToList());
        }
    }
    
    private void DeselectAll()
    {
        _selectedCard?.SetSelectedState(false);
        _selectedCard = null;
        
        foreach (var card in _selectedTableCards)
            card.SetSelectedState(false);
        _selectedTableCards.Clear();
        
        foreach (var card in GetCardsIn(_table))
            card.SetDisabledState(false);
    }
    
    private void EnableOnlyCards(List<CardUI> enabled)
    {
        foreach (var card in GetCardsIn(_table))
            card.SetDisabledState(!enabled.Contains(card));
    }
    
    #endregion

    #region Game Actions
    
    private void ExecuteThrow(CardUI card)
    {
        SendAction(card.CardData.ToString());
    }
    
    private void ExecuteCapture(CardUI handCard, List<CardUI> tableCards)
    {
        var captured = string.Join("+", tableCards.Select(c => c.CardData.ToString()));
        SendAction($"{handCard.CardData}x{captured}");
    }
    
    private void SendAction(string action)
    {
        GD.Print($"Action: {action}");
        _network.SendAction(action);
        DeselectAll();
        _isPlayerTurn = false;
        _waitingLabel.Show();
    }
    
    #endregion

    #region State Synchronization
    
    private async void OnGameStateReceived(Godot.Collections.Dictionary data)
    {
        if (!data.ContainsKey("state")) return;
        
        var state = data["state"].As<Godot.Collections.Dictionary>();
        _isPlayerTurn = bool.Parse(state["isMyTurn"].ToString());
        
        // Hide menu, show game
        _menuPanel.Hide();
        _waitingLabel.Hide();
        UpdateTurnDisplay();
        
        if (!_isPlayerTurn) DeselectAll();
        
        // Animate last move if present
        string lastMove = GetString(state, "lastMovePgn");
        if (!string.IsNullOrEmpty(lastMove))
        {
            string mover = _isPlayerTurn ? OpponentId : PlayerId;
            await AnimateLastMove(lastMove, mover);
        }
        
        // Sync board state
        SyncGameState(state);
    }
    
    private void SyncGameState(Godot.Collections.Dictionary state)
    {
        var players = state["players"].As<Godot.Collections.Dictionary>();
        
        // Sync hands
        var myData = players[PlayerId].As<Godot.Collections.Dictionary>();
        var oppData = players[OpponentId].As<Godot.Collections.Dictionary>();
        
        string[] myHand = GetStringArray(myData, "hand");
        string[] oppHand = GetStringArray(oppData, "hand");
        string[] tableCards = GetStringArray(state, "table");
        
        SyncCardGroup(myHand, _playerHand);
        SyncCardGroup(oppHand, _opponentHand);
        SyncCardGroup(tableCards, _table);
        
        // Update deck display
        var deck = state.ContainsKey("deck") ? state["deck"].As<Godot.Collections.Array>() : new();
        UpdateDeckDisplay(deck.Count);
        
        // Update capture counts
        _playerCaptureCount.Text = _playerCaptured.Count.ToString();
        _opponentCaptureCount.Text = _opponentCaptured.Count.ToString();
        
        // Clean up removed cards
        CleanupArea(_playerHand, myHand);
        CleanupArea(_opponentHand, oppHand);
        CleanupArea(_table, tableCards);
    }
    
    private void SyncCardGroup(string[] codes, Control area)
    {
        var center = area.Size / 2;
        int hiddenCount = 0;
        
        for (int i = 0; i < codes.Length; i++)
        {
            string code = codes[i];
            CardUI card = GetOrCreateCard(code, area, ref hiddenCount);
            
            Vector2 targetPos = CalculateCardPosition(i, codes.Length, center);
            AnimateCardToPosition(card, area.GlobalPosition + targetPos, i * AnimCardStagger);
        }
    }
    
    private CardUI GetOrCreateCard(string code, Control area, ref int hiddenCount)
    {
        // Known card already tracked
        if (code != "X" && _cardRegistry.TryGetValue(code, out var existing))
        {
            if (existing.GetParent() != area)
                existing.Reparent(area);
            return existing;
        }
        
        // Hidden card (opponent's)
        if (code == "X")
        {
            var hidden = FindHiddenCard(area, hiddenCount++);
            if (hidden != null) return hidden;
        }
        
        // Create new card
        var card = SpawnCard(code, area);
        if (code != "X") _cardRegistry[code] = card;
        return card;
    }
    
    private CardUI FindHiddenCard(Control area, int index)
    {
        int found = 0;
        foreach (var child in area.GetChildren())
        {
            if (child is CardUI card && card.CardData?.ToString() == "X")
            {
                if (found == index) return card;
                found++;
            }
        }
        return null;
    }
    
    private Vector2 CalculateCardPosition(int index, int total, Vector2 center)
    {
        float spacing = Mathf.Min(CardWidth, center.X * 2 / (total + 1));
        float totalWidth = (total - 1) * spacing + CardWidth;
        float startX = center.X - totalWidth / 2;
        float y = center.Y - CardHeight / 2;
        
        return new Vector2(startX + index * spacing, y);
    }
    
    private void CleanupArea(Control area, string[] validCodes)
    {
        int validHidden = validCodes.Count(c => c == "X");
        int hiddenSeen = 0;
        
        foreach (var child in area.GetChildren())
        {
            if (child is not CardUI card || !IsInstanceValid(card.CardData)) continue;
            
            string code = card.CardData.ToString();
            
            if (code == "X")
            {
                if (++hiddenSeen > validHidden)
                    card.QueueFree();
            }
            else if (!validCodes.Contains(code))
            {
                _cardRegistry.Remove(code);
                card.QueueFree();
            }
        }
    }
    
    #endregion

    #region Animations
    
    private async Task AnimateLastMove(string pgn, string moverId)
    {
        var parts = pgn.Split("x");
        string playedCode = parts[0];
        string[] capturedCodes = parts.Length > 1 ? parts[1].Split("+") : System.Array.Empty<string>();
        
        // Get the played card
        CardUI playedCard = GetPlayedCard(playedCode, moverId);
        if (playedCard == null) return;
        
        if (capturedCodes.Length == 0)
        {
            await AnimateThrow(playedCard);
        }
        else
        {
            await AnimateCapture(playedCard, capturedCodes, moverId == PlayerId);
        }
    }
    
    private CardUI GetPlayedCard(string code, string moverId)
    {
        if (moverId == PlayerId)
        {
            return _cardRegistry.GetValueOrDefault(code);
        }
        
        // Opponent's card - pick one from their hand and reveal it
        var oppCards = GetCardsIn(_opponentHand);
        if (oppCards.Count == 0) return null;
        
        var card = oppCards[(int)(GD.Randi() % oppCards.Count)];
        card.Setup(new CardData(code));
        card.ShowCardFront();
        _cardRegistry[code] = card;
        
        return card;
    }
    
    private async Task AnimateThrow(CardUI card)
    {
        var tableCards = GetCardsIn(_table);
        var center = _table.Size / 2;
        int newTotal = tableCards.Count + 1;
        
        Vector2 targetPos = CalculateCardPosition(tableCards.Count, newTotal, center);
        
        var tween = CreateTween();
        tween.TweenProperty(card, "global_position", _table.GlobalPosition + targetPos, AnimPlayCard)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        
        await ToSignal(tween, Tween.SignalName.Finished);
    }
    
    private async Task AnimateCapture(CardUI playedCard, string[] capturedCodes, bool isPlayer)
    {
        // Gather captured card nodes
        var captured = capturedCodes
            .Where(c => _cardRegistry.ContainsKey(c))
            .Select(c => _cardRegistry[c])
            .ToList();
        
        // Quick swoop: played card visits each captured card
        var swoopTween = CreateTween();
        foreach (var target in captured)
        {
            swoopTween.TweenProperty(playedCard, "global_position", target.GlobalPosition, AnimCollectCards)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
        }
        await ToSignal(swoopTween, Tween.SignalName.Finished);
        
        // All cards fly to capture pile
        var pile = isPlayer ? _playerCapturePile : _opponentCapturePile;
        var captureList = isPlayer ? _playerCaptured : _opponentCaptured;
        var pileCenter = new Vector2(
            pile.Size.X / 2 - CardWidth / 2,
            pile.Size.Y / 2 - CardHeight / 2
        );
        
        var allCards = new List<CardUI> { playedCard };
        allCards.AddRange(captured);
        
        var flyTween = CreateTween().SetParallel(true);
        foreach (var card in allCards)
        {
            card.Reparent(pile);
            card.ShowCardBack();
            
            flyTween.TweenProperty(card, "position", pileCenter, AnimCollectCards)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
        }
        
        await ToSignal(flyTween, Tween.SignalName.Finished);
        captureList.AddRange(allCards);
    }
    
    private void AnimateCardToPosition(CardUI card, Vector2 globalTarget, float delay)
    {
        if (card.GlobalPosition.DistanceTo(globalTarget) < 5f) return;
        
        var tween = CreateTween();
        if (delay > 0) tween.TweenInterval(delay);
        
        tween.TweenProperty(card, "global_position", globalTarget, AnimDealCard)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
    }
    
    #endregion

    #region UI Updates
    
    private void UpdateTurnDisplay()
    {
        _turnIndicator.Show();
        
        if (_isPlayerTurn)
        {
            _turnLabel.Text = "YOUR TURN";
            _turnLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.6f));
        }
        else
        {
            _turnLabel.Text = "OPPONENT'S TURN";
            _turnLabel.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.5f));
        }
    }
    
    private void UpdateDeckDisplay(int count)
    {
        _deckCountLabel.Text = count.ToString();
        
        int visible = Mathf.Min(DeckVisualStackSize, Mathf.CeilToInt(count / 6f));
        for (int i = 0; i < _deckVisuals.Count; i++)
            _deckVisuals[i].Visible = i < visible && count > 0;
    }
    
    #endregion

    #region Helpers
    
    private string OpponentId => PlayerId == "p1" ? "p2" : "p1";
    
    private List<CardUI> GetCardsIn(Control area)
    {
        return area.GetChildren()
            .OfType<CardUI>()
            .Where(c => !c.IsQueuedForDeletion())
            .ToList();
    }
    
    private CardUI SpawnCard(string code, Node parent)
    {
        var card = _cardScene.Instantiate<CardUI>();
        parent.AddChild(card);
        card.GlobalPosition = _deckPosition.GlobalPosition;
        card.Setup(new CardData(code));
        card.CardUiClicked += OnCardClicked;
        return card;
    }
    
    private List<CardUI> FindCardsValidForSum(int target, List<CardUI> pool)
    {
        return pool.Where(card =>
        {
            if (card.CardData.Rank > target) return false;
            if (card.CardData.Rank == target) return true;
            
            var remaining = pool.Where(c => c != card).ToList();
            return CanSumTo(target - card.CardData.Rank, remaining);
        }).ToList();
    }
    
    private bool CanSumTo(int target, List<CardUI> pool)
    {
        if (target == 0) return true;
        if (target < 0 || pool.Count == 0) return false;
        
        var first = pool[0];
        var rest = pool.Skip(1).ToList();
        
        return CanSumTo(target - first.CardData.Rank, rest) || CanSumTo(target, rest);
    }
    
    private static string GetString(Godot.Collections.Dictionary dict, string key)
    {
        return dict.ContainsKey(key) && dict[key].VariantType != Variant.Type.Nil
            ? dict[key].AsString()
            : "";
    }
    
    private static string[] GetStringArray(Godot.Collections.Dictionary dict, string key)
    {
        return dict.ContainsKey(key) ? dict[key].AsStringArray() : System.Array.Empty<string>();
    }
    
    #endregion
}
