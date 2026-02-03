using Godot;
using Scopa2Game.Scripts.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Scopa2Game.Scripts;

public partial class MainGame : Control
{
    private const string MyPlayerId = "p2";
    private const string CardBackPath = "res://assets/textures/deck/scopaback.png";
    private PackedScene _cardScene;

    // Nodes - anchors are now Control nodes for responsive layout
    private Control _opponentHandAnchor;
    private Control _tableAnchor;
    private Control _myHandAnchor;
    private Control _deckPos;
    private Control _playerCapturedAnchor;
    private Control _opponentCapturedAnchor;
    private Label _deckCountLabel;
    private Button _startButton;
    private Label _waitingLabel;
    private LineEdit _joinGameLineEdit;
    private Button _joinGameButton;
    private Panel _menuPanel;
    private Panel _turnIndicator;
    private Label _turnLabel;
    private Label _playerCaptureCountLabel;
    private Label _opponentCaptureCountLabel;
    
    // Deck visualization
    private List<TextureRect> _deckVisualCards = new();
    private const int MaxDeckVisualCards = 6;

    // State Management
    private Dictionary<string, CardUI> _cardNodes = new();
    private List<CardUI> _playerCapturedNodes = new();
    private List<CardUI> _opponentCapturedNodes = new();
    private bool _isMyTurn = false;

    // Input state machine
    private CardUI _selectedHandCard = null;
    private List<CardUI> _selectedTableCards = new();

    private NetworkManager _networkManager;

    public override void _Ready()
    {
        _cardScene = GD.Load<PackedScene>("res://card.tscn");

        // Get anchors from new structure
        _opponentHandAnchor = GetNode<Control>("GameArea/OpponentHandAnchor");
        _tableAnchor = GetNode<Control>("GameArea/TableAnchor");
        _myHandAnchor = GetNode<Control>("GameArea/MyHandAnchor");
        _deckPos = GetNode<Control>("GameArea/DeckArea/VBoxContainer/DeckVisualContainer/DeckPosition");
        _playerCapturedAnchor = GetNode<Control>("GameArea/PlayerCapturedArea/VBox/PlayerCapturedAnchor");
        _opponentCapturedAnchor = GetNode<Control>("GameArea/OpponentCapturedArea/VBox/OpponentCapturedAnchor");
        
        // UI Elements
        _deckCountLabel = GetNode<Label>("GameArea/DeckArea/VBoxContainer/DeckCountLabel");
        _startButton = GetNode<Button>("UI/MenuPanel/VBoxContainer/StartButton");
        _waitingLabel = GetNode<Label>("UI/WaitingLabel");
        _joinGameLineEdit = GetNode<LineEdit>("UI/MenuPanel/VBoxContainer/JoinContainer/JoinGameLineEdit");
        _joinGameButton = GetNode<Button>("UI/MenuPanel/VBoxContainer/JoinContainer/JoinGameButton");
        _menuPanel = GetNode<Panel>("UI/MenuPanel");
        _turnIndicator = GetNode<Panel>("UI/TurnIndicator");
        _turnLabel = GetNode<Label>("UI/TurnIndicator/TurnLabel");
        _playerCaptureCountLabel = GetNode<Label>("GameArea/PlayerCapturedArea/VBox/PlayerCaptureCount");
        _opponentCaptureCountLabel = GetNode<Label>("GameArea/OpponentCapturedArea/VBox/OpponentCaptureCount");

        // Initialize deck visualization
        InitializeDeckVisual();

        _networkManager = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        if (!IsInstanceValid(_networkManager))
        {
            GD.PrintErr("MainGame: NetworkManager autoload not found.");
            return;
        }

        _networkManager.StateUpdated += OnServerStateUpdated;
        _startButton.Pressed += OnStartButtonPressed;
        _joinGameButton.Pressed += OnJoinButtonPressed;
    }
    
    private void InitializeDeckVisual()
    {
        var deckTexture = GD.Load<Texture2D>(CardBackPath);
        
        for (int i = 0; i < MaxDeckVisualCards; i++)
        {
            var cardBack = new TextureRect
            {
                Texture = deckTexture,
                CustomMinimumSize = new Vector2(60, 84),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(-30 + i * 1.5f, -42 + i * -2f),
                Modulate = new Color(1, 1, 1, 1f - (i * 0.05f))
            };
            
            // Add slight rotation for stacked effect
            cardBack.Rotation = Mathf.DegToRad(-2 + i * 0.8f);
            
            _deckPos.AddChild(cardBack);
            _deckVisualCards.Add(cardBack);
        }
        
        UpdateDeckVisual(40);
    }
    
    private void UpdateDeckVisual(int deckSize)
    {
        _deckCountLabel.Text = deckSize.ToString();
        
        // Show/hide deck cards based on remaining cards
        int visibleCards = Mathf.Min(MaxDeckVisualCards, Mathf.CeilToInt(deckSize / 6.0f));
        
        for (int i = 0; i < _deckVisualCards.Count; i++)
        {
            _deckVisualCards[i].Visible = i < visibleCards && deckSize > 0;
        }
        
        // Subtle pulse animation when deck is low
        if (deckSize > 0 && deckSize <= 6)
        {
            foreach (var card in _deckVisualCards)
            {
                if (card.Visible)
                {
                    var tween = CreateTween();
                    tween.SetLoops(2);
                    tween.TweenProperty(card, "modulate", new Color(1.2f, 1.1f, 0.9f, 1), 0.3f);
                    tween.TweenProperty(card, "modulate", new Color(1, 1, 1, 1), 0.3f);
                }
            }
        }
    }
    
    private void UpdateTurnIndicator()
    {
        _turnIndicator.Visible = true;
        
        if (_isMyTurn)
        {
            _turnLabel.Text = "YOUR TURN";
            _turnLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.6f, 1f));
        }
        else
        {
            _turnLabel.Text = "OPPONENT'S TURN";
            _turnLabel.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.5f, 1f));
        }
    }
    
    private void UpdateCaptureCountLabels()
    {
        _playerCaptureCountLabel.Text = _playerCapturedNodes.Count.ToString();
        _opponentCaptureCountLabel.Text = _opponentCapturedNodes.Count.ToString();
    }

    private void OnJoinButtonPressed()
    {
        if (!IsInstanceValid(_joinGameLineEdit) || string.IsNullOrEmpty(_joinGameLineEdit.Text))
        {
            GD.PrintErr("MainGame: No game id entered to join.");
            return;
        }

        string gameId = _joinGameLineEdit.Text.Trim();
        GD.Print($"MainGame: Joining game {gameId}");
        _networkManager.JoinGame(gameId);
        // Hide menu panel
        _menuPanel.Hide();
        _waitingLabel.Visible = true;
    }

    // Input Handling
    
    private void OnCardClicked(CardUI cardNode)
    {
        if (!_isMyTurn) return;

        if (cardNode.GetParent() == _myHandAnchor)
        {
            HandleHandCardSelection(cardNode);
        }
        else if (cardNode.GetParent() == _tableAnchor)
        {
            HandleTableCardSelection(cardNode);
        }
    }

    private void HandleHandCardSelection(CardUI cardNode)
    {
        if (_selectedHandCard == cardNode)
        {
            _selectedHandCard.SetSelectedState(false);
            _selectedHandCard = null;
            ResetTableSelection();
            return;
        }

        if (IsInstanceValid(_selectedHandCard))
        {
            _selectedHandCard.SetSelectedState(false);
            ResetTableSelection();
        }

        _selectedHandCard = cardNode;
        _selectedHandCard.SetSelectedState(true);

        var tableCards = GetTableCards();
        int rank = cardNode.CardData.Rank;

        var directMatches = tableCards.Where(c => c.CardData.Rank == rank).ToList();

        if (directMatches.Any())
        {
            SetTableCardsInteractive(directMatches);
            return;
        }

        var validSubsetCards = GetCardsValidForSum(rank, tableCards);

        if (!validSubsetCards.Any())
        {
            PerformActionThrow(cardNode);
        }
        else
        {
            SetTableCardsInteractive(validSubsetCards);
        }
    }

    private void HandleTableCardSelection(CardUI cardNode)
    {
        if (!IsInstanceValid(_selectedHandCard)) return;
        if (cardNode.Disabled) return;

        if (_selectedTableCards.Contains(cardNode))
        {
            cardNode.SetSelectedState(false);
            _selectedTableCards.Remove(cardNode);
        }
        else
        {
            cardNode.SetSelectedState(true);
            _selectedTableCards.Add(cardNode);
        }

        int currentSum = _selectedTableCards.Sum(c => c.CardData.Rank);

        if (currentSum == _selectedHandCard.CardData.Rank)
        {
            PerformActionCapture(_selectedHandCard, _selectedTableCards);
        }
        else if (currentSum < _selectedHandCard.CardData.Rank)
        {
            int needed = _selectedHandCard.CardData.Rank - currentSum;
            var remainingTable = GetTableCards().Where(c => !_selectedTableCards.Contains(c)).ToList();
            var validNext = GetCardsValidForSum(needed, remainingTable);

            var allInteractive = new List<CardUI>(_selectedTableCards);
            allInteractive.AddRange(validNext);
            SetTableCardsInteractive(allInteractive);
        }
    }

    private void PerformActionThrow(CardUI cardNode)
    {
        string actionStr = cardNode.CardData.ToString();
        GD.Print("Auto-throwing: ", actionStr);
        SendNetworkAction(actionStr);
    }

    private void PerformActionCapture(CardUI handCard, List<CardUI> targets)
    {
        string handStr = handCard.CardData.ToString();
        var tableStrs = targets.Select(c => c.CardData.ToString());
        string targetsStr = string.Join("+", tableStrs);
        string actionStr = $"{handStr}x{targetsStr}";

        GD.Print("Auto-capturing: ", actionStr);
        SendNetworkAction(actionStr);
    }

    private void SendNetworkAction(string actionStr)
    {
        _networkManager.SendAction(actionStr);
        ResetSelection();
        _isMyTurn = false;
        _waitingLabel.Visible = true;
    }

    private void ResetSelection()
    {
        if (IsInstanceValid(_selectedHandCard))
        {
            _selectedHandCard.SetSelectedState(false);
        }
        ResetTableSelection();
        _selectedHandCard = null;
    }

    private void ResetTableSelection()
    {
        foreach (var card in _selectedTableCards)
        {
            card.SetSelectedState(false);
        }
        _selectedTableCards.Clear();
        foreach (var card in GetTableCards())
        {
            card.SetDisabledState(false);
        }
    }

    private List<CardUI> GetTableCards()
    {
        var cards = new List<CardUI>();
        foreach (var child in _tableAnchor.GetChildren())
        {
            if (child is CardUI c && !c.IsQueuedForDeletion())
            {
                cards.Add(c);
            }
        }
        return cards;
    }

    private void SetTableCardsInteractive(List<CardUI> enabledCards)
    {
        var all = GetTableCards();
        foreach (var c in all)
        {
            c.SetDisabledState(!enabledCards.Contains(c));
        }
    }

    private List<CardUI> GetCardsValidForSum(int target, List<CardUI> pool)
    {
        var valid = new List<CardUI>();
        for (int i = 0; i < pool.Count; i++)
        {
            var c = pool[i];
            if (c.CardData.Rank > target) continue;
            if (c.CardData.Rank == target)
            {
                valid.Add(c);
                continue;
            }

            var remainingPool = new List<CardUI>(pool);
            remainingPool.RemoveAt(i);
            if (CanSum(target - c.CardData.Rank, remainingPool))
            {
                valid.Add(c);
            }
        }
        return valid;
    }

    private bool CanSum(int target, List<CardUI> pool)
    {
        if (target == 0) return true;
        if (target < 0) return false;
        if (pool.Count == 0) return false;

        var first = pool[0];
        var rest = pool.Skip(1).ToList();

        if (CanSum(target - first.CardData.Rank, rest)) return true;
        if (CanSum(target, rest)) return true;

        return false;
    }

    // State Sync

    private async void OnServerStateUpdated(Godot.Collections.Dictionary serverData)
    {
        if (!serverData.ContainsKey("state")) return;
        var gameState = serverData["state"].As<Godot.Collections.Dictionary>();

        _isMyTurn = bool.Parse(gameState["isMyTurn"].ToString());
        _waitingLabel.Visible = false;
        _menuPanel.Visible = false;
        
        UpdateTurnIndicator();

        if (!_isMyTurn)
        {
            ResetSelection();
        }

        string pgn = gameState.ContainsKey("lastMovePgn") && gameState["lastMovePgn"].VariantType != Variant.Type.Nil 
            ? gameState["lastMovePgn"].AsString() : "";
        
        if (!string.IsNullOrEmpty(pgn))
        {
            string moverId =   _isMyTurn ? _getOpponentId() : _getMyPlayerId();
            await AnimateMove(pgn, moverId);
            GD.Print("Animation of last move complete.");
        }

        GD.Print("Reconciling state...");
        ReconcileState(gameState);
    }
    
    private string _getOpponentId()
    {
        return MyPlayerId == "p1" ? "p2" : "p1";
    }
    
    private string _getMyPlayerId()
    {
        return MyPlayerId;
    }

    private async Task AnimateMove(string pgn, string moverId)
    {
        GD.Print("Animating move: ", pgn, " by ", moverId);

        var parts = pgn.Split("x");
        string playedCardCode = parts[0];
        string[] capturedCodes = parts.Length > 1 ? parts[1].Split("+") : System.Array.Empty<string>();

        CardUI playedCardNode = null;

        if (moverId == MyPlayerId)
        {
            if (_cardNodes.ContainsKey(playedCardCode))
            {
                playedCardNode = _cardNodes[playedCardCode];
            }
        }
        else
        {
            var oppCards = GetCardsInAnchor(_opponentHandAnchor);
            if (oppCards.Count > 0)
            {
                // Simple random pick for visual effect
                int idx = (int)(GD.Randi() % oppCards.Count);
                playedCardNode = oppCards[idx];
                
                string oldCode = playedCardNode.Name; // Likely "OPP..."
                _cardNodes.Remove(oldCode); // Key might not be in dict if it was "X" or "Card"
                
                // We need to re-key it if it was tracked.
                // In Setup, it might have been tracked as something else.
                // Actually, opponent cards are likely "X"s or unknown.
                
                playedCardNode.Setup(new CardData(playedCardCode));
                _cardNodes[playedCardCode] = playedCardNode;
                playedCardNode.ShowCardFront();
            }
        }

        if (!IsInstanceValid(playedCardNode))
        {
            GD.PrintErr("Could not find card node for animation: ", playedCardCode);
            return;
        }

        if (capturedCodes.Length == 0)
        {
            var handTween = CreateTween();
            var targetAnchor = _tableAnchor;
            var tableCards = GetTableCards();
            
            // Calculate position relative to anchor center
            Vector2 anchorSize = targetAnchor.Size;
            Vector2 anchorCenter = anchorSize / 2;
            float cardWidth = Mathf.Min(95, anchorSize.X / (tableCards.Count + 2));
            float totalWidth = (tableCards.Count + 1) * cardWidth;
            float startX = anchorCenter.X - totalWidth / 2.0f + cardWidth / 2.0f;
            float cardHeight = playedCardNode.CustomMinimumSize.Y;
            float yPos = anchorCenter.Y - cardHeight / 2.0f;
            Vector2 targetPos = new Vector2(startX + tableCards.Count * cardWidth, yPos);

            handTween.TweenProperty(playedCardNode, "global_position", targetAnchor.GlobalPosition + targetPos, 0.4f)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            
            await ToSignal(handTween, Tween.SignalName.Finished);
        }
        else
        {
            var capturePile = moverId == MyPlayerId ? _playerCapturedAnchor : _opponentCapturedAnchor;
            var capturedNodes = new List<CardUI>();

            foreach (var code in capturedCodes)
            {
                if (_cardNodes.ContainsKey(code))
                {
                    capturedNodes.Add(_cardNodes[code]);
                }
            }

            var visitTween = CreateTween();
            foreach (var target in capturedNodes)
            {
                visitTween.TweenProperty(playedCardNode, "global_position", target.GlobalPosition, 0.3f)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);
                visitTween.TweenInterval(0.1f);
            }

            await ToSignal(visitTween, Tween.SignalName.Finished);

            var flyTween = CreateTween().SetParallel(true);
            var allMoving = new List<CardUI> { playedCardNode };
            allMoving.AddRange(capturedNodes);
            
            // Position cards in capture pile centered
            Vector2 pileCenter = capturePile.Size / 2;

            for(int i=0; i < allMoving.Count; i++)
            {
                var c = allMoving[i];
                c.Reparent(capturePile);
                
                // Stack cards with slight offset for visual depth
                Vector2 stackPos = new Vector2(pileCenter.X - 30, pileCenter.Y - 40 - 2 * (capturePile.GetChildCount() + i));
                flyTween.TweenProperty(c, "position", stackPos, 0.4f)
                    .SetTrans(Tween.TransitionType.Back)
                    .SetEase(Tween.EaseType.InOut);
            }

            await ToSignal(flyTween, Tween.SignalName.Finished);

            if (moverId == MyPlayerId)
                _playerCapturedNodes.AddRange(allMoving);
            else
                _opponentCapturedNodes.AddRange(allMoving);
        }
    }

    private void ReconcileState(Godot.Collections.Dictionary gameState)
    {
        var allStateCodes = new List<string>();

        // My Hand
        var players = gameState.ContainsKey("players") ? gameState["players"].As<Godot.Collections.Dictionary>() : new Godot.Collections.Dictionary();
        var myPlayer = players.ContainsKey(MyPlayerId) ? players[MyPlayerId].As<Godot.Collections.Dictionary>() : new Godot.Collections.Dictionary();
        var myHandCodes = myPlayer.ContainsKey("hand") ? myPlayer["hand"].AsStringArray() : System.Array.Empty<string>();
        SyncAnchorGroup(myHandCodes, _myHandAnchor);
        allStateCodes.AddRange(myHandCodes);

        // Opponent Hand
        string opponentId = _getOpponentId();
        var oppPlayer = players.ContainsKey(opponentId) ? players[opponentId].As<Godot.Collections.Dictionary>() : new Godot.Collections.Dictionary();
        var oppHandCodes = oppPlayer.ContainsKey("hand") ? oppPlayer["hand"].AsStringArray() : System.Array.Empty<string>();
        SyncAnchorGroup(oppHandCodes, _opponentHandAnchor);
        allStateCodes.AddRange(oppHandCodes);

        // Table
        var tableCodes = gameState.ContainsKey("table") ? gameState["table"].AsStringArray() : System.Array.Empty<string>();
        SyncAnchorGroup(tableCodes, _tableAnchor);
        allStateCodes.AddRange(tableCodes);

        // Deck - update visual representation
        var deckArray = gameState.ContainsKey("deck") ? gameState["deck"].As<Godot.Collections.Array>() : new Godot.Collections.Array();
        UpdateDeckVisual(deckArray.Count);
        
        // Update capture counts
        UpdateCaptureCountLabels();

        // Cleanup
        CleanupAnchor(_myHandAnchor, myHandCodes);
        CleanupAnchor(_opponentHandAnchor, oppHandCodes);
        CleanupAnchor(_tableAnchor, tableCodes);
    }

    private void SyncAnchorGroup(string[] codes, Control anchor)
    {
        int xCount = 0;
        Vector2 anchorSize = anchor.Size;
        Vector2 anchorCenter = anchorSize / 2;
        
        for (int i = 0; i < codes.Length; i++)
        {
            string code = codes[i];
            CardUI cardNode = null;

            if (_cardNodes.ContainsKey(code) && code != "X")
            {
                cardNode = _cardNodes[code];
                if (cardNode.GetParent() != anchor)
                {
                    cardNode.Reparent(anchor);
                }
            }
            else if (code == "X")
            {
                var existingX = FindAvailableXNode(anchor, xCount);
                xCount++;
                if (existingX != null)
                {
                    cardNode = existingX;
                }
                else
                {
                    cardNode = CreateCard(code, anchor, _deckPos.GlobalPosition);
                }
            }
            else
            {
                cardNode = CreateCard(code, anchor, _deckPos.GlobalPosition);
                _cardNodes[code] = cardNode;
            }

            // Calculate card size and spacing dynamically
            float cardWidth = Mathf.Min(95, anchorSize.X / (codes.Length + 1));
            float cardHeight = cardNode.CustomMinimumSize.Y;
            float totalWidth = codes.Length * cardWidth;
            float startX = anchorCenter.X - totalWidth / 2.0f + cardWidth / 2.0f;
            float yPos = anchorCenter.Y - cardHeight / 2.0f;
            Vector2 targetPos = new Vector2(startX + i * cardWidth, yPos);

            if (cardNode.Position.DistanceTo(targetPos) > 5.0f)
            {
                cardNode.AnimateMove(anchor.GlobalPosition + targetPos, i * 0.05f);
            }
        }
    }

    private CardUI FindAvailableXNode(Control anchor, int index)
    {
        var xs = new List<CardUI>();
        foreach (var c in anchor.GetChildren())
        {
            if (c is CardUI card && card.CardData.ToString() == "X")
            {
                xs.Add(card);
            }
        }
        if (index < xs.Count) return xs[index];
        return null;
    }

    private void CleanupAnchor(Control anchor, string[] validCodes)
    {
        int validXCount = validCodes.Count(c => c == "X");
        int currentXCount = 0;

        foreach (var child in anchor.GetChildren())
        {
            if (child is CardUI card)
            {
                if (!IsInstanceValid(card.CardData)) continue;
                string code = card.CardData.ToString();

                if (code == "X")
                {
                    currentXCount++;
                    if (currentXCount > validXCount)
                    {
                        card.QueueFree();
                    }
                }
                else if (!validCodes.Contains(code))
                {
                    _cardNodes.Remove(code);
                    card.QueueFree();
                }
            }
        }
    }

    private List<CardUI> GetCardsInAnchor(Control anchor)
    {
        var list = new List<CardUI>();
        foreach (var c in anchor.GetChildren())
        {
            if (c is CardUI card) list.Add(card);
        }
        return list;
    }

    private void OnStartButtonPressed()
    {
        _networkManager.StartGame();
        _menuPanel.Hide();
        _waitingLabel.Visible = true;
    }

    private CardUI CreateCard(string cardCode, Node parent, Vector2 startPos)
    {
        var cardData = new CardData(cardCode);
        var cardNode = _cardScene.Instantiate<CardUI>();
        parent.AddChild(cardNode);
        cardNode.GlobalPosition = startPos;
        cardNode.Setup(cardData);
        cardNode.CardUiClicked += OnCardClicked;
        return cardNode;
    }
}
