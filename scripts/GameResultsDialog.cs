using System.Collections.Generic;
using Godot;
using Scopa2Game.Scripts.Models;

namespace Scopa2Game.Scripts;

/// <summary>
/// A full-screen overlay dialog for end-of-game results.
/// Shows the final round breakdown (same as RoundResultsDialog) plus
/// cumulative game scores and the winner announcement.
/// Built entirely in code — no .tscn file needed.
/// </summary>
public partial class GameResultsDialog : CanvasLayer
{
    public event System.Action Closed;

    private GameFinished _data;
    private string _playerKey;
    private string _opponentKey;

    /// <summary>
    /// Call once before adding to the tree to populate the dialog.
    /// </summary>
    public void Populate(GameFinished data, string playerKey, string opponentKey)
    {
        _data = data;
        _playerKey = playerKey;
        _opponentKey = opponentKey;
    }

    public override void _Ready()
    {
        Layer = 100;

        // === Full-screen backdrop ===
        var backdrop = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.65f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        // === Center panel ===
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(460, 0);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.06f, 0.04f, 0.95f),
            BorderWidthLeft = 3,
            BorderWidthTop = 3,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            BorderColor = new Color(0.75f, 0.55f, 0.15f, 0.95f),
            CornerRadiusTopLeft = 16,
            CornerRadiusTopRight = 16,
            CornerRadiusBottomLeft = 16,
            CornerRadiusBottomRight = 16,
            ShadowColor = new Color(0, 0, 0, 0.7f),
            ShadowSize = 18,
            ContentMarginLeft = 28,
            ContentMarginRight = 28,
            ContentMarginTop = 24,
            ContentMarginBottom = 28
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        backdrop.AddChild(panel);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;

        // === Main VBox ===
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        // ─── Winner banner ───
        AddWinnerBanner(vbox);

        AddSeparator(vbox, new Color(0.75f, 0.55f, 0.15f, 0.5f));

        // ─── Final Game Scores ───
        AddGameScoresSection(vbox);

        AddSeparator(vbox, new Color(0.75f, 0.55f, 0.15f, 0.5f));

        // ─── Last Round Breakdown (same table as RoundResultsDialog) ───
        AddRoundBreakdownSection(vbox);

        // ─── Close button ───
        var closeBtnContainer = new HBoxContainer();
        closeBtnContainer.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(closeBtnContainer);

        var closeBtn = new Button
        {
            Text = "CLOSE",
            CustomMinimumSize = new Vector2(140, 42)
        };
        closeBtn.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.7f, 1f));
        closeBtn.AddThemeFontSizeOverride("font_size", 16);
        var closeBtnStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.4f, 0.25f, 0.08f, 0.85f),
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
            BorderColor = new Color(0.75f, 0.55f, 0.15f, 0.6f)
        };
        closeBtn.AddThemeStyleboxOverride("normal", closeBtnStyle);
        var closeBtnHover = (StyleBoxFlat)closeBtnStyle.Duplicate();
        closeBtnHover.BgColor = new Color(0.55f, 0.35f, 0.1f, 0.95f);
        closeBtnHover.BorderColor = new Color(1f, 0.8f, 0.3f, 0.9f);
        closeBtn.AddThemeStyleboxOverride("hover", closeBtnHover);
        closeBtn.AddThemeStyleboxOverride("pressed", closeBtnStyle);
        closeBtn.Pressed += () =>
        {
            Closed?.Invoke();
            QueueFree();
        };
        closeBtnContainer.AddChild(closeBtn);
    }

    // ──────────────────────────────────────────────────────────────
    //  SECTIONS
    // ──────────────────────────────────────────────────────────────

    private void AddWinnerBanner(VBoxContainer vbox)
    {
        bool isPlayerWinner = _data?.Winner == _playerKey;
        bool isDraw = string.IsNullOrEmpty(_data?.Winner);

        string bannerText;
        Color bannerColor;
        string emoji;

        if (isDraw)
        {
            bannerText = "IT'S A DRAW!";
            bannerColor = new Color(1f, 0.85f, 0.4f, 1f);
            emoji = "\u2696"; // balance scale
        }
        else if (isPlayerWinner)
        {
            bannerText = "YOU WIN!";
            bannerColor = new Color(0.4f, 1f, 0.5f, 1f);
            emoji = "\uD83C\uDFC6"; // trophy
        }
        else
        {
            bannerText = "YOU LOSE";
            bannerColor = new Color(1f, 0.45f, 0.4f, 1f);
            emoji = "\uD83D\uDE14"; // pensive face
        }

        // Title: "GAME OVER"
        var gameOverLabel = new Label
        {
            Text = "GAME OVER",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        gameOverLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.6f, 0.4f, 0.8f));
        gameOverLabel.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(gameOverLabel);

        // Winner line with emoji
        var winnerLabel = new Label
        {
            Text = $"{emoji}  {bannerText}  {emoji}",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        winnerLabel.AddThemeColorOverride("font_color", bannerColor);
        winnerLabel.AddThemeFontSizeOverride("font_size", 30);
        winnerLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        vbox.AddChild(winnerLabel);
    }

    private void AddGameScoresSection(VBoxContainer vbox)
    {
        // Section header
        var header = new Label
        {
            Text = "FINAL SCORES",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        header.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.45f, 0.9f));
        header.AddThemeFontSizeOverride("font_size", 18);
        header.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        vbox.AddChild(header);

        int myGameScore = 0;
        int oppGameScore = 0;
        _data?.GameScores.TryGetValue(_playerKey, out myGameScore);
        _data?.GameScores.TryGetValue(_opponentKey, out oppGameScore);

        // Score display in a row
        var scoreRow = new HBoxContainer();
        scoreRow.AddThemeConstantOverride("separation", 12);
        scoreRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(scoreRow);

        // Player score box
        bool playerWins = myGameScore > oppGameScore;
        AddScoreBox(scoreRow, "YOU", myGameScore.ToString(), playerWins,
            new Color(0.5f, 1f, 0.6f, 1f), new Color(0.15f, 0.25f, 0.12f, 0.8f));

        // VS label
        var vsLabel = new Label
        {
            Text = "vs",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        vsLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.55f, 0.4f, 0.6f));
        vsLabel.AddThemeFontSizeOverride("font_size", 16);
        scoreRow.AddChild(vsLabel);

        // Opponent score box
        AddScoreBox(scoreRow, "OPP", oppGameScore.ToString(), !playerWins && oppGameScore != myGameScore,
            new Color(1f, 0.7f, 0.5f, 1f), new Color(0.28f, 0.15f, 0.08f, 0.8f));
    }

    private static void AddScoreBox(HBoxContainer parent, string label, string score,
        bool isWinner, Color accentColor, Color bgColor)
    {
        var box = new PanelContainer();
        box.CustomMinimumSize = new Vector2(120, 0);

        var boxStyle = new StyleBoxFlat
        {
            BgColor = bgColor,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
            BorderWidthLeft = isWinner ? 2 : 1,
            BorderWidthTop = isWinner ? 2 : 1,
            BorderWidthRight = isWinner ? 2 : 1,
            BorderWidthBottom = isWinner ? 2 : 1,
            BorderColor = isWinner ? accentColor : new Color(0.4f, 0.3f, 0.2f, 0.4f)
        };
        box.AddThemeStyleboxOverride("panel", boxStyle);
        parent.AddChild(box);

        var boxVbox = new VBoxContainer();
        boxVbox.AddThemeConstantOverride("separation", 4);
        box.AddChild(boxVbox);

        var nameLabel = new Label
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        nameLabel.AddThemeColorOverride("font_color", accentColor);
        nameLabel.AddThemeFontSizeOverride("font_size", 13);
        boxVbox.AddChild(nameLabel);

        var scoreLabel = new Label
        {
            Text = score,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        scoreLabel.AddThemeColorOverride("font_color", isWinner ? accentColor : new Color(0.85f, 0.8f, 0.7f, 0.9f));
        scoreLabel.AddThemeFontSizeOverride("font_size", 28);
        scoreLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        boxVbox.AddChild(scoreLabel);

        if (isWinner)
        {
            var crownLabel = new Label
            {
                Text = "\u2605", // star
                HorizontalAlignment = HorizontalAlignment.Center
            };
            crownLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f, 0.9f));
            crownLabel.AddThemeFontSizeOverride("font_size", 14);
            boxVbox.AddChild(crownLabel);
        }
    }

    private void AddRoundBreakdownSection(VBoxContainer vbox)
    {
        // Section header
        var header = new Label
        {
            Text = "LAST ROUND BREAKDOWN",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        header.AddThemeColorOverride("font_color", new Color(0.8f, 0.7f, 0.5f, 0.7f));
        header.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(header);

        // Last capture info
        if (!string.IsNullOrEmpty(_data?.LastCapturePlayer))
        {
            bool isPlayerLastCapture = _data.LastCapturePlayer == _playerKey;
            var lastCapLabel = new Label
            {
                Text = isPlayerLastCapture
                    ? "You took the remaining table cards!"
                    : "Opponent took the remaining table cards.",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            lastCapLabel.AddThemeColorOverride("font_color",
                isPlayerLastCapture ? new Color(0.5f, 1f, 0.6f, 0.9f) : new Color(1f, 0.7f, 0.5f, 0.9f));
            lastCapLabel.AddThemeFontSizeOverride("font_size", 12);
            vbox.AddChild(lastCapLabel);
        }

        // Score comparison table
        if (_data?.RoundScores != null)
        {
            _data.RoundScores.TryGetValue(_playerKey, out var myScores);
            _data.RoundScores.TryGetValue(_opponentKey, out var oppScores);

            // Column headers
            CreateRow(vbox, "", "YOU", "OPP", isHeader: true);
            AddSeparator(vbox, new Color(0.5f, 0.35f, 0.15f, 0.3f));

            AddScoreRow(vbox, "Cards Captured",
                myScores?.CardsCaptured.ToString() ?? "-",
                oppScores?.CardsCaptured.ToString() ?? "-",
                myScores?.CardsCaptured > oppScores?.CardsCaptured);

            AddScoreRow(vbox, "Denari",
                FormatBoolCount(myScores?.Denari ?? false, myScores?.DenariCount ?? 0),
                FormatBoolCount(oppScores?.Denari ?? false, oppScores?.DenariCount ?? 0),
                myScores?.Denari ?? false);

            AddScoreRow(vbox, "Settebello",
                FormatBool(myScores?.Settebello ?? false),
                FormatBool(oppScores?.Settebello ?? false),
                myScores?.Settebello ?? false);

            AddScoreRow(vbox, "Primiera",
                FormatBool(myScores?.Primiera ?? false),
                FormatBool(oppScores?.Primiera ?? false),
                myScores?.Primiera ?? false);

            AddScoreRow(vbox, "Scope",
                myScores?.ScopaCount.ToString() ?? "0",
                oppScores?.ScopaCount.ToString() ?? "0",
                (myScores?.ScopaCount ?? 0) > (oppScores?.ScopaCount ?? 0));

            AddScoreRow(vbox, "Allungo",
                FormatBool(myScores?.Allungo ?? false),
                FormatBool(oppScores?.Allungo ?? false),
                myScores?.Allungo ?? false);

            // Round total
            AddSeparator(vbox, new Color(0.5f, 0.35f, 0.15f, 0.3f));

            int myTotal = CalcTotal(myScores);
            int oppTotal = CalcTotal(oppScores);

            AddScoreRow(vbox, "ROUND TOTAL",
                myTotal.ToString(),
                oppTotal.ToString(),
                myTotal > oppTotal,
                isTotal: true);
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  HELPERS (same as RoundResultsDialog)
    // ──────────────────────────────────────────────────────────────

    private static string FormatBool(bool val) => val ? "\u2714" : "\u2718";

    private static string FormatBoolCount(bool won, int count) =>
        won ? $"\u2714 ({count})" : $"\u2718 ({count})";

    private static int CalcTotal(PlayerRoundScores s)
    {
        if (s == null) return 0;
        int total = 0;
        if (s.Settebello) total++;
        if (s.Primiera) total++;
        if (s.Allungo) total++;
        if (s.Denari) total++;
        total += s.ScopaCount;
        return total;
    }

    private static void AddSeparator(VBoxContainer parent, Color? color = null)
    {
        var sep = new HSeparator();
        sep.AddThemeStyleboxOverride("separator", new StyleBoxFlat
        {
            BgColor = color ?? new Color(0.5f, 0.35f, 0.15f, 0.4f),
            ContentMarginTop = 1,
            ContentMarginBottom = 1
        });
        parent.AddChild(sep);
    }

    private static HBoxContainer CreateRow(VBoxContainer parent, string label, string col1, string col2,
        bool isHeader = false)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        parent.AddChild(row);

        int fontSize = isHeader ? 12 : 13;
        var labelColor = isHeader
            ? new Color(0.7f, 0.65f, 0.5f, 0.7f)
            : new Color(0.85f, 0.8f, 0.65f, 1f);

        var lbl = new Label
        {
            Text = label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2
        };
        lbl.AddThemeColorOverride("font_color", labelColor);
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        row.AddChild(lbl);

        var c1 = new Label
        {
            Text = col1,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        c1.AddThemeColorOverride("font_color", isHeader
            ? new Color(0.5f, 0.9f, 0.6f, 0.9f)
            : Colors.White);
        c1.AddThemeFontSizeOverride("font_size", fontSize);
        row.AddChild(c1);

        var c2 = new Label
        {
            Text = col2,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        c2.AddThemeColorOverride("font_color", isHeader
            ? new Color(1f, 0.7f, 0.5f, 0.9f)
            : Colors.White);
        c2.AddThemeFontSizeOverride("font_size", fontSize);
        row.AddChild(c2);

        return row;
    }

    private static void AddScoreRow(VBoxContainer parent, string label, string myVal, string oppVal,
        bool playerWins, bool isTotal = false)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        parent.AddChild(row);

        int fontSize = isTotal ? 15 : 13;

        var lbl = new Label
        {
            Text = label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2
        };
        lbl.AddThemeColorOverride("font_color",
            isTotal ? new Color(1f, 0.9f, 0.6f, 1f) : new Color(0.85f, 0.8f, 0.65f, 1f));
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        row.AddChild(lbl);

        var c1 = new Label
        {
            Text = myVal,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        c1.AddThemeColorOverride("font_color",
            playerWins ? new Color(0.4f, 1f, 0.5f, 1f) : new Color(0.8f, 0.75f, 0.65f, 0.8f));
        c1.AddThemeFontSizeOverride("font_size", fontSize);
        row.AddChild(c1);

        var c2 = new Label
        {
            Text = oppVal,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        c2.AddThemeColorOverride("font_color",
            !playerWins ? new Color(1f, 0.6f, 0.45f, 1f) : new Color(0.8f, 0.75f, 0.65f, 0.8f));
        c2.AddThemeFontSizeOverride("font_size", fontSize);
        row.AddChild(c2);
    }
}
