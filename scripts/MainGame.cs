using Godot;
using Scopa2Game.Scripts.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Scopa2Game.Scripts;

public partial class MainGame : Node2D
{
    private const string MyPlayerId = "p1";
    private PackedScene _cardScene;

    // Nodes
    private Node2D _opponentHandAnchor;
    private Node2D _tableAnchor;
    private Node2D _myHandAnchor;
    private Node2D _deckPos;
    private Node2D _playerCapturedAnchor;
    private Node2D _opponentCapturedAnchor;
    private Label _deckCountLabel;
    private Button _startButton;
    private Label _waitingLabel;
    private LineEdit _joinGameLineEdit;
    private Button _joinGameButton;

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

        _opponentHandAnchor = GetNode<Node2D>("OpponentHandAnchor");
        _tableAnchor = GetNode<Node2D>("TableAnchor");
        _myHandAnchor = GetNode<Node2D>("MyHandAnchor");
        _deckPos = GetNode<Node2D>("DeckPosition");
        _playerCapturedAnchor = GetNode<Node2D>("PlayerCapturedAnchor");
        _opponentCapturedAnchor = GetNode<Node2D>("OpponentCapturedAnchor");
        _deckCountLabel = GetNode<Label>("UI/DeckCountLabel");
        _startButton = GetNode<Button>("UI/StartButton");
        _waitingLabel = GetNode<Label>("UI/WaitingLabel");
        _joinGameLineEdit = GetNode<LineEdit>("UI/JoinGameLineEdit");
        _joinGameButton = GetNode<Button>("UI/JoinGameButton");

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
        // Hide start/join UI to avoid confusion
        _startButton.Hide();
        _joinGameButton.Hide();
        _joinGameLineEdit.Hide();
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

        string currentPlayer = gameState.ContainsKey("currentTurnPlayer") ? gameState["currentTurnPlayer"].AsString() : "";
        _isMyTurn = (currentPlayer == MyPlayerId);
        _waitingLabel.Visible = !_isMyTurn;

        if (!_isMyTurn)
        {
            ResetSelection();
        }

        string pgn = gameState.ContainsKey("lastMovePgn") && gameState["lastMovePgn"].VariantType != Variant.Type.Nil 
            ? gameState["lastMovePgn"].AsString() : "";
        
        if (!string.IsNullOrEmpty(pgn))
        {
            string moverId = _isMyTurn ? "p2" : "p1";
            await AnimateMove(pgn, moverId);
            GD.Print("Animation of last move complete.");
        }

        GD.Print("Reconciling state...");
        ReconcileState(gameState);
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
            float cardWidth = 95;
            float totalWidth = (tableCards.Count + 1) * cardWidth;
            float startX = -totalWidth / 2.0f + cardWidth / 2.0f;
            Vector2 targetPos = new Vector2(startX + tableCards.Count * cardWidth, 0);

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

            for(int i=0; i < allMoving.Count; i++)
            {
                var c = allMoving[i];
                c.Reparent(capturePile);
                flyTween.TweenProperty(c, "position", new Vector2(0, -2 * (capturePile.GetChildCount() + i)), 0.4f)
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
        string opponentId = MyPlayerId == "p1" ? "p2" : "p1";
        var oppPlayer = players.ContainsKey(opponentId) ? players[opponentId].As<Godot.Collections.Dictionary>() : new Godot.Collections.Dictionary();
        var oppHandCodes = oppPlayer.ContainsKey("hand") ? oppPlayer["hand"].AsStringArray() : System.Array.Empty<string>();
        SyncAnchorGroup(oppHandCodes, _opponentHandAnchor);
        allStateCodes.AddRange(oppHandCodes);

        // Table
        var tableCodes = gameState.ContainsKey("table") ? gameState["table"].AsStringArray() : System.Array.Empty<string>();
        SyncAnchorGroup(tableCodes, _tableAnchor);
        allStateCodes.AddRange(tableCodes);

        // Deck
        var deckArray = gameState.ContainsKey("deck") ? gameState["deck"].As<Godot.Collections.Array>() : new Godot.Collections.Array();
        _deckCountLabel.Text = $"Deck: {deckArray.Count}";

        // Cleanup
        CleanupAnchor(_myHandAnchor, myHandCodes);
        CleanupAnchor(_opponentHandAnchor, oppHandCodes);
        CleanupAnchor(_tableAnchor, tableCodes);
    }

    private void SyncAnchorGroup(string[] codes, Node2D anchor)
    {
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
                var existingX = FindAvailableXNode(anchor, i);
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

            float cardWidth = 95;
            float totalWidth = codes.Length * cardWidth;
            float startX = -totalWidth / 2.0f + cardWidth / 2.0f;
            Vector2 targetPos = new Vector2(startX + i * cardWidth, 0);

            if (cardNode.Position.DistanceTo(targetPos) > 5.0f)
            {
                cardNode.AnimateMove(anchor.GlobalPosition + targetPos, i * 0.05f);
            }
        }
    }

    private CardUI FindAvailableXNode(Node2D anchor, int index)
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

    private void CleanupAnchor(Node2D anchor, string[] validCodes)
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

    private List<CardUI> GetCardsInAnchor(Node2D anchor)
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
        _startButton.Hide();
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
