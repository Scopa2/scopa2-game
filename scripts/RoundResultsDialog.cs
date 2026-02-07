using System.Collections.Generic;
using Godot;
using Scopa2Game.Scripts.Models;

namespace Scopa2Game.Scripts;

/// <summary>
/// A semi-transparent overlay dialog that shows round results.
/// Dismissable via an X button; game events continue in the background.
/// Built entirely in code — no .tscn file needed.
/// </summary>
public partial class RoundResultsDialog : CanvasLayer
{
    private RoundFinished _data;
    private string _playerKey;
    private string _opponentKey;

    /// <summary>
    /// Call once before adding to the tree (or right after) to populate the dialog.
    /// </summary>
    public void Populate(RoundFinished data, string playerKey, string opponentKey)
    {
        _data = data;
        _playerKey = playerKey;
        _opponentKey = opponentKey;
    }

    public override void _Ready()
    {
        Layer = 100; // render on top of everything

        // --- Full-screen semi-transparent backdrop ---
        var backdrop = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop // block clicks from reaching the game
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        // --- Center panel ---
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(420, 0);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

        // Style the panel
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.08f, 0.06f, 0.92f),
            BorderWidthLeft = 3,
            BorderWidthTop = 3,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            BorderColor = new Color(0.6f, 0.42f, 0.15f, 0.9f),
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ShadowColor = new Color(0, 0, 0, 0.6f),
            ShadowSize = 12,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 20,
            ContentMarginBottom = 24
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        backdrop.AddChild(panel);

        // Center the panel in the backdrop
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;

        // --- Main VBox ---
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        panel.AddChild(vbox);

        // --- Header row (title + close button) ---
        var headerRow = new HBoxContainer();
        vbox.AddChild(headerRow);

        var title = new Label
        {
            Text = "ROUND RESULTS",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.45f, 1f));
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        headerRow.AddChild(title);

        var closeBtn = new Button { Text = "X", CustomMinimumSize = new Vector2(36, 36) };
        closeBtn.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.6f, 1f));
        closeBtn.AddThemeFontSizeOverride("font_size", 18);
        var closeBtnStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.3f, 0.1f, 0.1f, 0.7f),
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 2,
            ContentMarginBottom = 2
        };
        closeBtn.AddThemeStyleboxOverride("normal", closeBtnStyle);
        var closeBtnHover = (StyleBoxFlat)closeBtnStyle.Duplicate();
        closeBtnHover.BgColor = new Color(0.5f, 0.15f, 0.15f, 0.9f);
        closeBtn.AddThemeStyleboxOverride("hover", closeBtnHover);
        closeBtn.AddThemeStyleboxOverride("pressed", closeBtnStyle);
        closeBtn.Pressed += () => QueueFree();
        headerRow.AddChild(closeBtn);

        // --- Separator ---
        AddSeparator(vbox);

        // --- Last capture info ---
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
            lastCapLabel.AddThemeFontSizeOverride("font_size", 13);
            vbox.AddChild(lastCapLabel);
        }

        // --- Score comparison table ---
        if (_data?.RoundScores != null)
        {
            _data.RoundScores.TryGetValue(_playerKey, out var myScores);
            _data.RoundScores.TryGetValue(_opponentKey, out var oppScores);

            // Column headers
            var headerGrid = CreateRow(vbox, "", "YOU", "OPP", isHeader: true);

            AddSeparator(vbox);

            // Rows for each scoring category
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

            // --- Total ---
            AddSeparator(vbox);

            int myTotal = CalcTotal(myScores);
            int oppTotal = CalcTotal(oppScores);

            AddScoreRow(vbox, "ROUND TOTAL",
                myTotal.ToString(),
                oppTotal.ToString(),
                myTotal > oppTotal,
                isTotal: true);
        }

        // --- Dismiss hint ---
        var hint = new Label
        {
            Text = "The game continues in the background.",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.55f, 0.45f, 0.6f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(hint);
    }

    // --- Helpers ---

    private static string FormatBool(bool val) => val ? "\u2714" : "\u2718"; // checkmark / cross

    private static string FormatBoolCount(bool won, int count) =>
        won ? $"\u2714 ({count})" : $"\u2718 ({count})";

    private static int CalcTotal(PlayerRoundScores s)
    {
        if (s == null) return 0;
        int total = 0;
        // Each bool category = 1 point if won
        if (s.Settebello) total++;
        if (s.Primiera) total++;
        if (s.Allungo) total++;
        if (s.Denari) total++;
        total += s.ScopaCount;
        return total;
    }

    private static void AddSeparator(VBoxContainer parent)
    {
        var sep = new HSeparator();
        sep.AddThemeStyleboxOverride("separator", new StyleBoxFlat
        {
            BgColor = new Color(0.5f, 0.35f, 0.15f, 0.4f),
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

        int fontSize = isHeader ? 13 : 14;
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

        int fontSize = isTotal ? 16 : 14;

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

        // Player column
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

        // Opponent column
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
