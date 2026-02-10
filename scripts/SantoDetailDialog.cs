using System;
using Godot;
using Scopa2Game.Scripts.Models;

namespace Scopa2Game.Scripts;

/// <summary>
/// Popup overlay showing a santo's details with an optional buy button.
/// Built entirely in code — same CanvasLayer pattern as RoundResultsDialog.
/// </summary>
public partial class SantoDetailDialog : CanvasLayer
{
    public enum DialogMode { Buy, Play }

    public event Action<string> BuyRequested;
    public event Action<string> PlayRequested;

    private ShopItem _item;
    private bool _canAct;
    private DialogMode _mode;

    /// <summary>
    /// Call before adding to tree.
    /// </summary>
    public void Populate(ShopItem item, bool isPlayerTurn, DialogMode mode = DialogMode.Buy)
    {
        _item = item;
        _canAct = isPlayerTurn;
        _mode = mode;
    }

    public override void _Ready()
    {
        Layer = 100;

        // --- Backdrop ---
        var backdrop = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        // --- Panel ---
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(340, 0);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.08f, 0.06f, 0.95f),
            BorderWidthLeft = 3,
            BorderWidthTop = 3,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            BorderColor = new Color(0.7f, 0.45f, 0.15f, 0.9f),
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ShadowColor = new Color(0, 0, 0, 0.6f),
            ShadowSize = 14,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 20,
            ContentMarginBottom = 24
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        backdrop.AddChild(panel);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;

        // --- Content ---
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        // Header row (title + close)
        var headerRow = new HBoxContainer();
        vbox.AddChild(headerRow);

        var title = new Label
        {
            Text = "\u2726 SANTO DETAILS",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.45f, 1f));
        title.AddThemeFontSizeOverride("font_size", 20);
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

        // Separator
        AddSeparator(vbox);

        // Santo name
        var nameLabel = new Label
        {
            Text = _item?.Name ?? "Unknown Santo",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.8f, 1f));
        nameLabel.AddThemeFontSizeOverride("font_size", 22);
        nameLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        vbox.AddChild(nameLabel);

        // Cost display (kept simple — currencies coming later)
        var costLabel = new Label
        {
            Text = $"Cost: {_item?.Cost ?? 0}",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        costLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f, 0.7f));
        costLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(costLabel);

        AddSeparator(vbox);

        // Description
        var descLabel = new RichTextLabel
        {
            BbcodeEnabled = false,
            Text = _item?.Description ?? "No description available.",
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(280, 0)
        };
        descLabel.AddThemeColorOverride("default_color", new Color(0.8f, 0.75f, 0.6f, 0.9f));
        descLabel.AddThemeFontSizeOverride("normal_font_size", 13);
        vbox.AddChild(descLabel);

        AddSeparator(vbox);

        // Action button (only if it's the player's turn)
        if (_canAct)
        {
            var actionBtnContainer = new HBoxContainer();
            actionBtnContainer.Alignment = BoxContainer.AlignmentMode.Center;
            vbox.AddChild(actionBtnContainer);

            string btnText = _mode == DialogMode.Play ? "✦  PLAY" : "✦  BUY";
            var actionBtn = new Button
            {
                Text = btnText,
                CustomMinimumSize = new Vector2(160, 44)
            };
            actionBtn.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.85f, 1f));
            actionBtn.AddThemeFontSizeOverride("font_size", 15);

            var actionBtnColor = _mode == DialogMode.Play
                ? new Color(0.15f, 0.4f, 0.18f, 0.9f)
                : new Color(0.6f, 0.18f, 0.12f, 0.9f);
            var actionBtnBorder = _mode == DialogMode.Play
                ? new Color(0.3f, 0.7f, 0.35f, 0.8f)
                : new Color(0.9f, 0.35f, 0.2f, 0.8f);

            var actionBtnStyle = new StyleBoxFlat
            {
                BgColor = actionBtnColor,
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
                BorderColor = actionBtnBorder
            };
            actionBtn.AddThemeStyleboxOverride("normal", actionBtnStyle);

            var actionBtnHover = (StyleBoxFlat)actionBtnStyle.Duplicate();
            actionBtnHover.BgColor = actionBtnColor with { R = actionBtnColor.R + 0.1f };
            actionBtnHover.BorderColor = actionBtnBorder with { A = 1f };
            actionBtn.AddThemeStyleboxOverride("hover", actionBtnHover);
            actionBtn.AddThemeStyleboxOverride("pressed", actionBtnStyle);

            actionBtn.Pressed += () =>
            {
                string id = _item?.Id ?? "";
                if (_mode == DialogMode.Play)
                    PlayRequested?.Invoke(id);
                else
                    BuyRequested?.Invoke(id);
                QueueFree();
            };
            actionBtnContainer.AddChild(actionBtn);
        }
        else
        {
            string hint = _mode == DialogMode.Play
                ? "Wait for your turn to play."
                : "Wait for your turn to buy.";
            var hintLabel = new Label
            {
                Text = hint,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            hintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.55f, 0.4f, 0.6f));
            hintLabel.AddThemeFontSizeOverride("font_size", 12);
            vbox.AddChild(hintLabel);
        }
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
}
