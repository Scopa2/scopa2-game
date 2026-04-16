using Godot;

namespace Scopa2Game.Scripts;

/// <summary>
/// Modal dialog displayed during server switching.
/// Shows loading indicator and target server region.
/// Non-dismissable during the switch process.
/// </summary>
public partial class ServerSwitchDialog : Control
{
    private Panel _panel;
    private Label _titleLabel;
    private Label _messageLabel;
    private ProgressBar _progressBar;
    private Timer _pulseTimer;
    private float _pulseDirection = 1f;

    public override void _Ready()
    {
        // Setup as overlay
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // Block clicks during switch
        
        // Semi-transparent background overlay
        var overlay = new ColorRect();
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.Color = new Color(0, 0, 0, 0.7f);
        AddChild(overlay);

        // CenterContainer fills the overlay and reliably centers its child
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        // Panel with a fixed size — CenterContainer handles the centering
        _panel = new Panel();
        _panel.CustomMinimumSize = new Vector2(420, 160);

        // Panel styling
        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.95f);
        panelStyle.BorderColor = new Color(0.8f, 0.6f, 0.2f);
        panelStyle.SetBorderWidthAll(3);
        panelStyle.SetCornerRadiusAll(10);
        panelStyle.ContentMarginLeft   = 20;
        panelStyle.ContentMarginRight  = 20;
        panelStyle.ContentMarginTop    = 16;
        panelStyle.ContentMarginBottom = 16;
        _panel.AddThemeStyleboxOverride("panel", panelStyle);
        center.AddChild(_panel);

        // VBox container for content
        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 15);
        _panel.AddChild(vbox);

        // Title
        _titleLabel = new Label();
        _titleLabel.Text = "Server Connection Issue";
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AddThemeFontSizeOverride("font_size", 18);
        _titleLabel.AddThemeColorOverride("font_color", new Color(1, 0.9f, 0.6f));
        vbox.AddChild(_titleLabel);

        // Message
        _messageLabel = new Label();
        _messageLabel.Text = "Switching to backup server...";
        _messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _messageLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _messageLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddChild(_messageLabel);

        // Progress bar (indeterminate)
        _progressBar = new ProgressBar();
        _progressBar.CustomMinimumSize = new Vector2(0, 20);
        _progressBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _progressBar.ShowPercentage = false;
        _progressBar.Value = 50;
        vbox.AddChild(_progressBar);

        // Pulse animation timer
        _pulseTimer = new Timer();
        _pulseTimer.WaitTime = 0.03f;
        _pulseTimer.Timeout += OnPulseTimeout;
        AddChild(_pulseTimer);

        Hide(); // Start hidden
    }

    private void OnPulseTimeout()
    {
        // Animate progress bar back and forth
        _progressBar.Value += _pulseDirection * 2;
        
        if (_progressBar.Value >= 100)
        {
            _pulseDirection = -1f;
        }
        else if (_progressBar.Value <= 0)
        {
            _pulseDirection = 1f;
        }
    }

    /// <summary>
    /// Shows the dialog with a message about switching to the target region.
    /// </summary>
    public void ShowSwitching(string targetRegion)
    {
        _messageLabel.Text = $"Switching to backup server: {targetRegion}...\nPlease wait.";
        _progressBar.Value = 50;
        _pulseTimer.Start();
        Show();
        GD.Print($"ServerSwitchDialog: Showing switch to {targetRegion}");
    }

    /// <summary>
    /// Hides the dialog after successful switch.
    /// </summary>
    public new void Hide()
    {
        _pulseTimer.Stop();
        base.Hide();
        GD.Print("ServerSwitchDialog: Hidden");
    }
}
