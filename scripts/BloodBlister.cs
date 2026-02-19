using Godot;

namespace Scopa2Game.Scripts;

/// <summary>
/// A visual "blood blister" gauge that shows liquid and solid blood levels.
/// Solid blood renders at the bottom in a darker colour; liquid blood sits on top.
/// Smooth tween animations interpolate between old and new values.
/// </summary>
public partial class BloodBlister : Control
{
    #region Constants

    private const int MaxBlood = 20;
    private const float AnimDuration = 0.45f;

    // Colours
    private static readonly Color LiquidBloodColor = new(0.75f, 0.08f, 0.08f, 0.92f);
    private static readonly Color SolidBloodColor = new(0.35f, 0.02f, 0.02f, 0.95f);
    private static readonly Color EmptyColor = new(0.12f, 0.06f, 0.06f, 0.55f);
    private static readonly Color GlassOutline = new(0.55f, 0.18f, 0.18f, 0.7f);
    private static readonly Color GlassHighlight = new(1f, 1f, 1f, 0.12f);
    private static readonly Color StepLineColor = new(0.9f, 0.35f, 0.35f, 0.18f);
    private static readonly Color LabelColor = new(0.95f, 0.85f, 0.8f, 0.9f);

    #endregion

    #region State

    // Display values (animated, 0..MaxBlood)
    private float _displayLiquid;
    private float _displaySolid;

    // Target values from server
    private int _targetBlood;
    private int _targetSolidBlood;

    private Tween _activeTween;

    #endregion

    public override void _Ready()
    {
        // Ensure we redraw every frame while animating
        SetProcess(false);
    }

    /// <summary>
    /// Call this when the game state updates.
    /// <paramref name="blood"/> – total blood (liquid + solid).
    /// <paramref name="solidBlood"/> – portion of blood that has solidified.
    /// </summary>
    public void SetBlood(int blood, int solidBlood)
    {
        blood = Mathf.Clamp(blood, 0, MaxBlood);
        solidBlood = Mathf.Clamp(solidBlood, 0, MaxBlood);

        if (blood == _targetBlood && solidBlood == _targetSolidBlood)
            return;

        _targetBlood = blood;
        _targetSolidBlood = solidBlood;

        float targetLiquid = blood - solidBlood;
        float targetSolid = solidBlood;

        // Kill any running tween
        _activeTween?.Kill();

        _activeTween = CreateTween();
        _activeTween.SetParallel(true);
        _activeTween.SetEase(Tween.EaseType.InOut);
        _activeTween.SetTrans(Tween.TransitionType.Cubic);

        _activeTween.TweenMethod(
            Callable.From((float v) => { _displayLiquid = v; QueueRedraw(); }),
            _displayLiquid, targetLiquid, AnimDuration);

        _activeTween.TweenMethod(
            Callable.From((float v) => { _displaySolid = v; QueueRedraw(); }),
            _displaySolid, targetSolid, AnimDuration);
    }

    public override void _Draw()
    {
        var rect = GetRect();
        float w = rect.Size.X;
        float h = rect.Size.Y;

        // Padding inside the blister outline
        float pad = 3f;
        float innerW = w - pad * 2;
        float innerH = h - pad * 2;

        float cornerRadius = Mathf.Min(innerW * 0.42f, 8f);

        // ── Background (empty chamber) ──────────────────────────────
        DrawBlisterShape(new Vector2(pad, pad), new Vector2(innerW, innerH), cornerRadius, EmptyColor);

        // ── Step lines ──────────────────────────────────────────────
        for (int i = 1; i < MaxBlood; i++)
        {
            float y = pad + innerH * (1f - (float)i / MaxBlood);
            DrawLine(new Vector2(pad + 2, y), new Vector2(pad + innerW - 2, y), StepLineColor, 1f);
        }

        // ── Solid blood (bottom) ────────────────────────────────────
        if (_displaySolid > 0.01f)
        {
            float solidFrac = Mathf.Clamp(_displaySolid / MaxBlood, 0f, 1f);
            float solidH = innerH * solidFrac;
            float solidY = pad + innerH - solidH;
            DrawBlisterFill(new Vector2(pad, solidY), new Vector2(innerW, solidH), cornerRadius, SolidBloodColor, true, solidFrac);
        }

        // ── Liquid blood (sits on top of solid) ─────────────────────
        if (_displayLiquid > 0.01f)
        {
            float solidFrac = Mathf.Clamp(_displaySolid / MaxBlood, 0f, 1f);
            float liquidFrac = Mathf.Clamp(_displayLiquid / MaxBlood, 0f, 1f);
            float liquidH = innerH * liquidFrac;
            float liquidY = pad + innerH - innerH * (solidFrac + liquidFrac);
            DrawBlisterFill(new Vector2(pad, liquidY), new Vector2(innerW, liquidH), cornerRadius, LiquidBloodColor, false, solidFrac + liquidFrac);
        }

        // ── Glass outline ───────────────────────────────────────────
        DrawBlisterOutline(new Vector2(pad, pad), new Vector2(innerW, innerH), cornerRadius, GlassOutline, 1.5f);

        // ── Glass highlight (shine strip on left) ───────────────────
        float shineX = pad + innerW * 0.18f;
        float shineTop = pad + innerH * 0.08f;
        float shineBot = pad + innerH * 0.88f;
        DrawLine(new Vector2(shineX, shineTop), new Vector2(shineX, shineBot), GlassHighlight, 2f);

        // ── Top / bottom caps (rounded bulge) ───────────────────────
        DrawBlisterCap(new Vector2(pad + innerW / 2, pad), innerW, true);
        DrawBlisterCap(new Vector2(pad + innerW / 2, pad + innerH), innerW, false);

        // ── Numeric label ───────────────────────────────────────────
        var font = ThemeDB.FallbackFont;
        int fontSize = Mathf.Max((int)(innerW * 0.36f), 8);
        string text = _targetBlood.ToString();
        Vector2 textSize = font.GetStringSize(text, HorizontalAlignment.Center, -1, fontSize);
        Vector2 textPos = new(
            pad + (innerW - textSize.X) / 2,
            pad + innerH * 0.08f + textSize.Y
        );
        // text shadow
        DrawString(font, textPos + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, fontSize,
            new Color(0, 0, 0, 0.6f));
        DrawString(font, textPos, text, HorizontalAlignment.Left, -1, fontSize, LabelColor);
    }

    #region Drawing Helpers

    /// <summary>Draws a filled rounded rectangle acting as the blister body.</summary>
    private void DrawBlisterShape(Vector2 origin, Vector2 size, float radius, Color color)
    {
        // top rounded rect
        var r = new Rect2(origin, size);
        DrawRoundedRect(r, radius, color, true);
    }

    /// <summary>Draws a filled region clipped to the blister bounds (for blood level).</summary>
    private void DrawBlisterFill(Vector2 origin, Vector2 size, float cornerRadius, Color color, bool isBottom, float totalFrac)
    {
        // When fill covers full width, we can just draw a rect; corners only matter at very top / bottom
        float r = 0f;
        if (isBottom || totalFrac >= 0.95f)
            r = cornerRadius;

        var rect = new Rect2(origin, size);
        if (r > 0.5f)
            DrawRoundedRect(rect, r, color, true);
        else
            DrawRect(rect, color);

        // Meniscus highlight at the top edge of this fill
        float meniscusY = origin.Y;
        DrawLine(
            new Vector2(origin.X + 2, meniscusY),
            new Vector2(origin.X + size.X - 2, meniscusY),
            new Color(1f, 0.7f, 0.7f, 0.18f), 1f);
    }

    /// <summary>Draws a rounded rect outline.</summary>
    private void DrawBlisterOutline(Vector2 origin, Vector2 size, float radius, Color color, float width)
    {
        var r = new Rect2(origin, size);
        DrawRoundedRect(r, radius, color, false, width);
    }

    /// <summary>Draws a small semicircular cap at the top or bottom of the blister.</summary>
    private void DrawBlisterCap(Vector2 center, float width, bool isTop)
    {
        float r = width * 0.35f;
        int segments = 12;
        float startAngle = isTop ? Mathf.Pi : 0f;
        float endAngle = isTop ? Mathf.Tau : Mathf.Pi;

        Vector2[] points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(startAngle, endAngle, t);
            points[i] = center + new Vector2(Mathf.Cos(angle) * (width * 0.38f), Mathf.Sin(angle) * r * 0.5f);
        }

        for (int i = 0; i < segments; i++)
        {
            DrawLine(points[i], points[i + 1], GlassOutline, 1.2f);
        }
    }

    /// <summary>Utility: draw a rounded rectangle (filled or outlined).</summary>
    private void DrawRoundedRect(Rect2 rect, float radius, Color color, bool filled, float outlineWidth = 1f)
    {
        // Clamp radius
        radius = Mathf.Min(radius, Mathf.Min(rect.Size.X, rect.Size.Y) * 0.5f);

        // Build points for a rounded rectangle path
        int cornerSegs = 6;
        Vector2[] path = new Vector2[(cornerSegs + 1) * 4];
        int idx = 0;

        // Corner centres
        Vector2 tl = rect.Position + new Vector2(radius, radius);
        Vector2 tr = rect.Position + new Vector2(rect.Size.X - radius, radius);
        Vector2 br = rect.Position + new Vector2(rect.Size.X - radius, rect.Size.Y - radius);
        Vector2 bl = rect.Position + new Vector2(radius, rect.Size.Y - radius);

        // top-left corner (PI .. 3PI/2)
        for (int i = 0; i <= cornerSegs; i++)
        {
            float t = (float)i / cornerSegs;
            float a = Mathf.Lerp(Mathf.Pi, Mathf.Pi * 1.5f, t);
            path[idx++] = tl + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        // top-right corner (3PI/2 .. 2PI)
        for (int i = 0; i <= cornerSegs; i++)
        {
            float t = (float)i / cornerSegs;
            float a = Mathf.Lerp(Mathf.Pi * 1.5f, Mathf.Tau, t);
            path[idx++] = tr + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        // bottom-right corner (0 .. PI/2)
        for (int i = 0; i <= cornerSegs; i++)
        {
            float t = (float)i / cornerSegs;
            float a = Mathf.Lerp(0f, Mathf.Pi * 0.5f, t);
            path[idx++] = br + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        // bottom-left corner (PI/2 .. PI)
        for (int i = 0; i <= cornerSegs; i++)
        {
            float t = (float)i / cornerSegs;
            float a = Mathf.Lerp(Mathf.Pi * 0.5f, Mathf.Pi, t);
            path[idx++] = bl + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        if (filled)
        {
            Color[] colors = new Color[path.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            DrawPolygon(path, colors);
        }
        else
        {
            // Draw outline using polyline (close the loop)
            Vector2[] closed = new Vector2[path.Length + 1];
            path.CopyTo(closed, 0);
            closed[^1] = path[0];
            DrawPolyline(closed, color, outlineWidth, true);
        }
    }

    #endregion
}
