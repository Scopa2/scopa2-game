using System;
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
    private bool _forceCapturedCardsFaceDown = true;
    private Task _activeAnimationTask = Task.CompletedTask;

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
        _opponentCaptureCount =
            GetNode<Label>("GameArea/OpponentCapturedArea/VBox/StatsCountsRow/OpponentCaptureCount");
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
        _network.RoundFinished += OnRoundFinished;
        _network.GameFinished += OnGameFinished;
        _network.NetworkError += OnNetworkError;
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
        
        private async void OnGameStateReceived(GameState state)
        {
            _activeAnimationTask = ProcessGameState(state);
            await _activeAnimationTask;
        }
    
        private async Task ProcessGameState(GameState state)
        {
            _isPlayerTurn = state.IsMyTurn;
            
            // Hide menu, show game
            _menuPanel.Hide();
            _waitingLabel.Hide();
            UpdateTurnDisplay();
            
            if (!_isPlayerTurn) DeselectAll();
            
            // Animate last move if present
            if (!string.IsNullOrEmpty(state.LastMovePgn))
            {
                var mover = _isPlayerTurn ? _opponentIndex : _playerIndex;
                await AnimateLastMove(state.LastMovePgn, mover);
            }
            
            // Sync board state
            SyncGameState(state);
        }
        
        private async void OnRoundFinished(RoundFinished finished)
        {
            RoundFinishedCleanTable(finished.LastCapturePlayer);
            ShowRoundResultsDialog(finished);
        }
        

        private async void OnGameFinished(GameFinished finished)
        {
            RoundFinishedCleanTable(finished.LastCapturePlayer);
            ShowGameResultsDialog(finished);
        }
    
         private async void RoundFinishedCleanTable(string lastCapturePlayer)
        {
            // 1. Determine who takes the cards
            bool isMyCapture = lastCapturePlayer == PlayerIndexString(_playerIndex);
            Control targetPile = isMyCapture ? _playerCapturePile : _opponentCapturePile;
    
            // 2. Lock interaction
            _isPlayerTurn = false;
            DeselectAll();
            
            // Ensure any pending move animations are finished
            await _activeAnimationTask;
    
            // 3. Gather cards currently in hands (if any)
            var myRemaining = GetCardsIn(_playerHand);
            var oppRemaining = GetCardsIn(_opponentHand);
    
            // 4. Phase 1: Drop remaining hand cards to table with stagger
            var allHandCards = new List<(CardUI card, bool isPlayer)>();
            foreach (var c in myRemaining) allHandCards.Add((c, true));
            foreach (var c in oppRemaining) allHandCards.Add((c, false));
    
            if (allHandCards.Count > 0)
            {
                for (int i = 0; i < allHandCards.Count; i++)
                {
                    var (card, isPlayer) = allHandCards[i];
                    _ = AnimateHandDropToTable(card, isPlayer, i * 0.08f);
                }
                // Wait for all drops to finish (last card delay + animation time)
                float totalDropTime = allHandCards.Count * 0.08f + AnimPlayCard + 0.05f;
                await ToSignal(GetTree().CreateTimer(totalDropTime), SceneTreeTimer.SignalName.Timeout);
    
                // Brief pause to let player see revealed cards
                await ToSignal(GetTree().CreateTimer(0.25f), SceneTreeTimer.SignalName.Timeout);
            }
    
            // 5. Phase 2: Gather ALL table cards to center, then sweep to pile
            var allTableCards = GetCardsIn(_table);
    
            if (allTableCards.Count > 0)
            {
                await AnimateTableGatherAndSweep(allTableCards, targetPile);
            }
    
            // 6. Visual Feedback
            ShowFloatingText(targetPile, "Round Cleared!", isMyCapture ? Colors.Green : Colors.Red);
        }
        private async Task AnimateHandDropToTable(CardUI card, bool isPlayer, float delay = 0f)
        {
            // 1. Visual setup: Show front if it was hidden (opponent's cards)
            if (!isPlayer)
            {
                card.ShowCardFront();
            }
    
            // 2. Reparent to table; preserve GlobalPosition to prevent snapping
            Vector2 oldGlobal = card.GlobalPosition;
            card.Reparent(_table);
            card.GlobalPosition = oldGlobal;
    
            // 3. Calculate a gentle scatter position near table center
            Random rng = new Random();
            float halfW = _table.Size.X * 0.3f;
            float halfH = _table.Size.Y * 0.25f;
            float scatterX = (float)(rng.NextDouble() * 2 - 1) * halfW;
            float scatterY = (float)(rng.NextDouble() * 2 - 1) * halfH;
            Vector2 targetPos = _table.GlobalPosition + (_table.Size / 2) + new Vector2(scatterX, scatterY);
    
            // 4. Tween with optional stagger delay
            var tween = CreateTween();
            if (delay > 0f) tween.TweenInterval(delay);
    
            // Smooth ease-out move
            tween.TweenProperty(card, "global_position", targetPos, AnimPlayCard)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
    
            // Gentle rotation (±0.15 rad)
            float randomRot = (float)(rng.NextDouble() * 0.3 - 0.15);
            tween.Parallel().TweenProperty(card, "rotation", randomRot, AnimPlayCard)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
    
            await ToSignal(tween, Tween.SignalName.Finished);
        }
    
        private async Task AnimateTableGatherAndSweep(List<CardUI> cards, Control targetPile)
        {
            // --- STEP 1: GATHER TO TABLE CENTER ---
            Vector2 tableCenter = _table.Size / 2;
            var gatherTween = CreateTween();
            
            // Stagger each card slightly for a cascading gather
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                card.ZIndex = 100 + i;
                
                // Tight stack at center
                Vector2 centerPos = new Vector2(
                    tableCenter.X - CardWidth / 2 + i * 0.4f,
                    tableCenter.Y - CardHeight / 2 - i * 0.4f
                );
                
                float staggerDelay = i * 0.04f;
                gatherTween.Parallel().TweenProperty(card, "position", centerPos, 0.35f)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.InOut)
                    .SetDelay(staggerDelay);
                gatherTween.Parallel().TweenProperty(card, "rotation", 0f, 0.3f)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out)
                    .SetDelay(staggerDelay);
            }
            
            await ToSignal(gatherTween, Tween.SignalName.Finished);
            
            // Brief pause to show gathered stack
            await ToSignal(GetTree().CreateTimer(0.2f), SceneTreeTimer.SignalName.Timeout);
            
            // --- STEP 2: SWEEP TO PILE ---
            int existingPileCount = GetCardsIn(targetPile).Count;
            Vector2 pileCenter = targetPile.Size / 2;
    
            // Reparent all cards first, preserving global position
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                Vector2 currentGlobal = card.GlobalPosition;
                card.Reparent(targetPile);
                card.GlobalPosition = currentGlobal;
                card.ShowCardBack();
            }
    
            // Animate the sweep with stagger
            var sweepTween = CreateTween();
    
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                int stackIndex = existingPileCount + i;
                
                Vector2 targetLocalPos = new Vector2(
                    pileCenter.X - CardWidth / 2 + stackIndex * 1.5f,
                    pileCenter.Y - CardHeight / 2 - stackIndex * 2f
                );
                float targetRot = Mathf.DegToRad(-2 + stackIndex * 0.8f);
                float staggerDelay = i * 0.03f;
    
                sweepTween.Parallel().TweenProperty(card, "position", targetLocalPos, 0.4f)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.InOut)
                    .SetDelay(staggerDelay);
                sweepTween.Parallel().TweenProperty(card, "rotation", targetRot, 0.4f)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out)
                    .SetDelay(staggerDelay);
    
                card.ZIndex = stackIndex;
            }
    
            await ToSignal(sweepTween, Tween.SignalName.Finished);
        }
    
        private void ShowFloatingText(Control target, string text, Color color)
        {
            var label = new Label();
            label.Text = text;
            label.AddThemeColorOverride("font_color", color);
            label.AddThemeFontSizeOverride("font_size", 24);
            label.AddThemeConstantOverride("outline_size", 4);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            
            GetTree().Root.AddChild(label);
            label.GlobalPosition = target.GlobalPosition + new Vector2(0, -30);
            label.Modulate = new Color(1, 1, 1, 0);
            label.Scale = new Vector2(0.6f, 0.6f);
            label.PivotOffset = label.Size / 2;
            
            var tween = CreateTween();
            // Fade in + scale up
            tween.TweenProperty(label, "modulate:a", 1f, 0.2f)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
            tween.Parallel().TweenProperty(label, "scale", Vector2.One, 0.25f)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            // Hold briefly
            tween.TweenInterval(0.6f);
            // Drift up + fade out
            tween.TweenProperty(label, "global_position", label.GlobalPosition + new Vector2(0, -40), 0.5f)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.In);
            tween.Parallel().TweenProperty(label, "modulate:a", 0f, 0.5f);
            tween.TweenCallback(Callable.From(label.QueueFree));
        }
        
        private void SyncGameState(GameState state)
        {
            // Sync hands
            var myData = state.Players[PlayerIndexString(_playerIndex)];
            var oppData = state.Players[PlayerIndexString(_opponentIndex)];
            
            // Ensure lists are not null (model initializes them, but good to be safe if deserialize fails weirdly)
            var myHand = myData.Hand;
            var oppHand = oppData.Hand;
            var tableCards = state.Table;
            var myCaptured = myData.Captured;
            var oppCaptured = oppData.Captured;
            var deckCards = state.Deck;
            
            // Detect if this is a round-end sweep (Table cleared on server, but we still have cards locally)
            // If so, we delay moving them to capture piles so OnRoundFinished can animate them.
            var currentTableCards = GetCardsIn(_table);
            bool isRoundSweep = tableCards.Count == 0 && currentTableCards.Count > 0;
            
            if (isRoundSweep)
            {
                // Filter out table cards from the captured lists so SyncCardGroup doesn't steal them yet
                var tableCodes = currentTableCards.Select(c => c.CardData.ToString()).ToHashSet();
                myCaptured = myCaptured.Where(c => !tableCodes.Contains(c)).ToList();
                oppCaptured = oppCaptured.Where(c => !tableCodes.Contains(c)).ToList();
            }
             
            SyncCardGroup(myHand, _playerHand);
            SyncCardGroup(oppHand, _opponentHand);
            
            // Only sync table if it's NOT a sweep (otherwise we keep cards there for animation)
            if (!isRoundSweep)
            {
                SyncCardGroup(tableCards, _table);
            }
            
            SyncCardGroup(myCaptured, _playerCapturePile);
            SyncCardGroup(oppCaptured, _opponentCapturePile);
            SyncCardGroup(deckCards, _deckPosition);
            
            // Update deck count display
            _deckCountLabel.Text = deckCards.Count.ToString();
            
            // Update capture counts
            _playerCaptureCount.Text = state.Players[PlayerIndexString(_playerIndex)].Captured.Count.ToString();
            _opponentCaptureCount.Text = state.Players[PlayerIndexString(_opponentIndex)].Captured.Count.ToString();
            
            // Update scope counts
            _playerScopeCount.Text = myData.Scope.ToString("0");
            _opponentScopeCount.Text = oppData.Scope.ToString("0");
            
            // Update scores
            _playerScoreLabel.Text = myData.TotalScore.ToString("0");
            _opponentScoreLabel.Text = oppData.TotalScore.ToString("0");
            
            // Clean up removed cards
            CleanupArea(_playerHand, myHand);
            CleanupArea(_opponentHand, oppHand);
            
            if (!isRoundSweep)
            {
                CleanupArea(_table, tableCards);
            }
            
            CleanupArea(_playerCapturePile, myCaptured);
            CleanupArea(_opponentCapturePile, oppCaptured);
            CleanupArea(_deckPosition, deckCards);
        }
        
        private void SyncCardGroup(List<string> codes, Control area)
        {
            var center = area.Size / 2;
            int hiddenCount = 0;
            
            // Check if this is a pile area (cards stack in center, don't spread)
            bool isPileArea = area == _playerCapturePile || area == _opponentCapturePile || area == _deckPosition;
            
            for (int i = 0; i < codes.Count; i++)
            {
                string code = codes[i];
                CardUI card = GetOrCreateCard(code, area, ref hiddenCount);
                
                // Set card visual state based on server response
                // If server sends "X", show back; otherwise show front
                // Also enforce face down for capture piles if enabled
                bool shouldBeFaceDown = code == "X";
                if (_forceCapturedCardsFaceDown && (area == _playerCapturePile || area == _opponentCapturePile))
                {
                    shouldBeFaceDown = true;
                }
    
                if (shouldBeFaceDown)
                {
                    card.ShowCardBack();
                }
                else
                {
                    card.ShowCardFront();
                }
                
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
                    
                    Vector2 targetPos = CalculateCardPosition(i, codes.Count, center);
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
        
        private void CleanupArea(Control area, List<string> validCodes)
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

    private void ShowRoundResultsDialog(RoundFinished finished)
    {
        var dialog = new RoundResultsDialog();
        dialog.Populate(finished, PlayerIndexString(_playerIndex), PlayerIndexString(_opponentIndex));
        AddChild(dialog);
    }

    private void ShowGameResultsDialog(GameFinished finished)
    {
        var dialog = new GameResultsDialog();
        dialog.Populate(finished, PlayerIndexString(_playerIndex), PlayerIndexString(_opponentIndex));
        dialog.Closed += () =>
        {
            _menuPanel.Show();
            _turnIndicator.Hide();
        };
        AddChild(dialog);
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

    private static string PlayerIndexString(PlayerIndex index)
    {
        return index == PlayerIndex.P1 ? "p1" : "p2";
    }

    #endregion

    #region Error Handling

    private void OnNetworkError(string errorMessage)
    {
        GD.PrintErr($"Network Error: {errorMessage}");
        _menuPanel.Show();
        _startButton.Text = "ERROR: " + errorMessage;
        _waitingLabel.Hide();
        _turnIndicator.Hide();
        DeselectAll();
    }

    #endregion
}