using Godot;
using Scopa2Game.Scripts.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Scopa2Game.Scripts.Enums;

namespace Scopa2Game.Scripts;

/// <summary>
/// Main game controller for Scopa card game.
/// Handles UI, game state synchronization, player input, and animations.
/// </summary>
public partial class MainGame : Control
{
    #region Constants

    private const string CardBackTexturePath = "res://assets/textures/deck/scopaback.png";
    private const float CardWidth = 95f;
    private const float CardHeight = 127f;
    
    // Animation durations (in seconds) - keeping them snappy
    private const float AnimDealCard = 0.25f;
    private const float AnimPlayCard = 0.2f;
    private const float AnimCollectCards = 0.25f;
    private const float AnimCardStagger = 0.1f;
    
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
    private Label _playerScopeCount;
    private Label _opponentScopeCount;
    private Label _playerScoreLabel;
    private Label _opponentScoreLabel;
    private Label _waitingLabel;
    private Button _startButton;
    private Button _joinButton;
    private LineEdit _gameIdInput;
    
    #endregion

    #region Game State
    
    private readonly Dictionary<string, CardUI> _cardRegistry = new();
    
    private PlayerIndex _playerIndex;
    private PlayerIndex _opponentIndex;
    private bool _isPlayerTurn;
    private CardUI _selectedCard;
    private readonly List<CardUI> _selectedTableCards = new();
    
    #endregion

    #region Initialization
    
    public override void _Ready()
    {
        CacheNodeReferences();
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
        _playerCaptureCount = GetNode<Label>("GameArea/PlayerCapturedArea/VBox/StatsCountsRow/PlayerCaptureCount");
        _opponentCaptureCount = GetNode<Label>("GameArea/OpponentCapturedArea/VBox/StatsCountsRow/OpponentCaptureCount");
        _playerScopeCount = GetNode<Label>("GameArea/PlayerCapturedArea/VBox/StatsCountsRow/PlayerScopeCount");
        _opponentScopeCount = GetNode<Label>("GameArea/OpponentCapturedArea/VBox/StatsCountsRow/OpponentScopeCount");
        _playerScoreLabel = GetNode<Label>("GameArea/PlayerCapturedArea/VBox/ScoreRow/PlayerScoreLabel");
        _opponentScoreLabel = GetNode<Label>("GameArea/OpponentCapturedArea/VBox/ScoreRow/OpponentScoreLabel");
        _waitingLabel = GetNode<Label>("UI/WaitingLabel");
        _startButton = GetNode<Button>("UI/MenuPanel/VBoxContainer/StartButton");
        _joinButton = GetNode<Button>("UI/MenuPanel/VBoxContainer/JoinContainer/JoinGameButton");
        _gameIdInput = GetNode<LineEdit>("UI/MenuPanel/VBoxContainer/JoinContainer/JoinGameLineEdit");
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
        _playerIndex = PlayerIndex.P1;
        _opponentIndex = PlayerIndex.P2;
    }
    
    private void OnJoinPressed()
    {
        var gameId = _gameIdInput.Text.Trim();
        if (string.IsNullOrEmpty(gameId)) return;
        
        _network.JoinGame(gameId);
        _menuPanel.Hide();
        _waitingLabel.Show();
        _playerIndex = PlayerIndex.P2;
        _opponentIndex = PlayerIndex.P1;
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
            var mover = _isPlayerTurn ? _opponentIndex : _playerIndex;
            await AnimateLastMove(lastMove, mover);
        }
        
        // Sync board state
        SyncGameState(state);
    }
    
    private void SyncGameState(Godot.Collections.Dictionary state)
    {
        var players = state["players"].As<Godot.Collections.Dictionary>();
        
        // Sync hands
        var myData = players[PlayerIndexString(_playerIndex)].As<Godot.Collections.Dictionary>();
        var oppData = players[PlayerIndexString(_opponentIndex)].As<Godot.Collections.Dictionary>();
        
        string[] myHand = GetStringArray(myData, "hand");
        string[] oppHand = GetStringArray(oppData, "hand");
        string[] tableCards = GetStringArray(state, "table");
        string[] myCaptured = GetStringArray(myData, "captured");
        string[] oppCaptured = GetStringArray(oppData, "captured");
        string[] deckCards = GetStringArray(state, "deck");
         
        SyncCardGroup(myHand, _playerHand);
        SyncCardGroup(oppHand, _opponentHand);
        SyncCardGroup(tableCards, _table);
        SyncCardGroup(myCaptured, _playerCapturePile);
        SyncCardGroup(oppCaptured, _opponentCapturePile);
        SyncCardGroup(deckCards, _deckPosition);
        
        // Update deck count display
        _deckCountLabel.Text = deckCards.Length.ToString();
        
        // Update capture counts
        _playerCaptureCount.Text = myCaptured.Length.ToString();
        _opponentCaptureCount.Text = oppCaptured.Length.ToString();
        
        // Update scope counts
        int myScope = myData.ContainsKey("scope") ? (int)double.Parse(myData["scope"].ToString(), System.Globalization.CultureInfo.InvariantCulture) : 0;
        int oppScope = oppData.ContainsKey("scope") ? (int)double.Parse(oppData["scope"].ToString(), System.Globalization.CultureInfo.InvariantCulture) : 0;
        _playerScopeCount.Text = myScope.ToString();
        _opponentScopeCount.Text = oppScope.ToString();
        
        // Update scores
        int myScore = myData.ContainsKey("totalScore") ? (int)double.Parse(myData["totalScore"].ToString(), System.Globalization.CultureInfo.InvariantCulture) : 0;
        int oppScore = oppData.ContainsKey("totalScore") ? (int)double.Parse(oppData["totalScore"].ToString(), System.Globalization.CultureInfo.InvariantCulture) : 0;
        _playerScoreLabel.Text = myScore.ToString();
        _opponentScoreLabel.Text = oppScore.ToString();
        
        // Clean up removed cards
        CleanupArea(_playerHand, myHand);
        CleanupArea(_opponentHand, oppHand);
        CleanupArea(_table, tableCards);
        CleanupArea(_playerCapturePile, myCaptured);
        CleanupArea(_opponentCapturePile, oppCaptured);
        CleanupArea(_deckPosition, deckCards);
    }
    
    private void SyncCardGroup(string[] codes, Control area)
    {
        var center = area.Size / 2;
        int hiddenCount = 0;
        
        // Check if this is a pile area (cards stack in center, don't spread)
        bool isPileArea = area == _playerCapturePile || area == _opponentCapturePile || area == _deckPosition;
        
        for (int i = 0; i < codes.Length; i++)
        {
            string code = codes[i];
            CardUI card = GetOrCreateCard(code, area, ref hiddenCount);
            
            if (isPileArea)
            {
                // For pile areas (captured cards and deck), stack cards with visual effect
                if (card.GetParent() != area)
                    card.Reparent(area);
                
                ApplyPileStackEffect(card, i, center);
            }
            else
            {
                // For hands and table, animate to spread positions
                // Reset any stacking effects from pile areas
                card.Rotation = 0f;
                card.Modulate = Colors.White;
                card.ZIndex = 0;
                
                Vector2 targetPos = CalculateCardPosition(i, codes.Length, center);
                AnimateCardToPosition(card, area.GlobalPosition + targetPos, i * AnimCardStagger);
            }
        }
    }
    
    private void ApplyPileStackEffect(CardUI card, int stackIndex, Vector2 center)
    {
        // Calculate stack position with slight offset for visual depth
        Vector2 stackPos = new Vector2(
            center.X - CardWidth / 2 + stackIndex * 1.5f,
            center.Y - CardHeight / 2 - stackIndex * 2f
        );
        
        // Apply rotation for visual variety
        float rotation = Mathf.DegToRad(-2 + stackIndex * 0.8f);
        
        // Fade older cards slightly
        float opacity = Mathf.Max(0.7f, 1f - stackIndex * 0.05f);
        
        card.Position = stackPos;
        card.Rotation = rotation;
        card.Modulate = new Color(1, 1, 1, opacity);
        card.ZIndex = stackIndex;
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
    
    private async Task AnimateLastMove(string pgn, PlayerIndex moverIndex)
    {
        var parts = pgn.Split("x");
        string playedCode = parts[0];
        string[] capturedCodes = parts.Length > 1 ? parts[1].Split("+") : System.Array.Empty<string>();
        
        // Get the played card
        CardUI playedCard = GetPlayedCard(playedCode, moverIndex);
        if (playedCard == null) return;
        
        if (capturedCodes.Length == 0)
        {
            await AnimateThrow(playedCard);
        }
        else
        {
            await AnimateCapture(playedCard, capturedCodes, moverIndex == _playerIndex);
        }
    }
    
    private CardUI GetPlayedCard(string code, PlayerIndex moverIndex)
    {
        if (moverIndex == _playerIndex)
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
        
        // Put capturing card on top of all captured cards
        playedCard.ZIndex = 100;
        
        // Swoop: played card visits each captured card with pause
        foreach (var target in captured)
        {
            var swoopTween = CreateTween();
            swoopTween.TweenProperty(playedCard, "global_position", target.GlobalPosition, AnimCollectCards)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
            await ToSignal(swoopTween, Tween.SignalName.Finished);
            
            // Small pause to highlight the captured card
            await ToSignal(GetTree().CreateTimer(0.15f), SceneTreeTimer.SignalName.Timeout);
        }
        
        // All cards fly to capture pile
        var pile = isPlayer ? _playerCapturePile : _opponentCapturePile;
        var center = pile.Size / 2;
        
        var allCards = new List<CardUI> { playedCard };
        allCards.AddRange(captured);
        
        // Get current pile size to determine stack index for new cards
        int existingPileSize = GetCardsIn(pile).Count;
        
        var flyTween = CreateTween().SetParallel(true);
        for (int i = 0; i < allCards.Count; i++)
        {
            var card = allCards[i];
            card.Reparent(pile);
            card.ShowCardBack();
            
            // Calculate stack position with twisted effect
            int stackIndex = existingPileSize + i;
            Vector2 stackPos = new Vector2(
                center.X - CardWidth / 2 + stackIndex * 1.5f,
                center.Y - CardHeight / 2 - stackIndex * 2f
            );
            float rotation = Mathf.DegToRad(-2 + stackIndex * 0.8f);
            float opacity = Mathf.Max(0.7f, 1f - stackIndex * 0.05f);
            
            flyTween.TweenProperty(card, "position", stackPos, AnimCollectCards)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
            flyTween.TweenProperty(card, "rotation", rotation, AnimCollectCards)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
            flyTween.TweenProperty(card, "modulate", new Color(1, 1, 1, opacity), AnimCollectCards)
                .SetTrans(Tween.TransitionType.Linear)
                .SetEase(Tween.EaseType.Out);
            
            card.ZIndex = stackIndex;
        }
        
        await ToSignal(flyTween, Tween.SignalName.Finished);
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
    
    #endregion

    #region Helpers
    
    
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
    
    private static string PlayerIndexString(PlayerIndex index)
    {
        return index == PlayerIndex.P1 ? "p1" : "p2";
    }
    
    #endregion
}
