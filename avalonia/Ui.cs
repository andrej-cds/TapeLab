using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CDSMeasure;

/// <summary>Barve in majhni gradniki v slogu cassettedeck-service.com (temna podlaga, zlat poudarek).</summary>
public static class Ui
{
    public static readonly IBrush Bg = Brush.Parse("#0A0A0A");
    public static readonly IBrush Panel = Brush.Parse("#141414");
    public static readonly IBrush Panel2 = Brush.Parse("#1B1B1B");
    public static readonly IBrush Line = Brush.Parse("#2E2A22");
    public static readonly IBrush Text = Brush.Parse("#F0EDE8");
    public static readonly IBrush Dim = Brush.Parse("#9A958C");
    public static readonly IBrush Gold = Brush.Parse("#C8A96E");
    public static readonly IBrush GoldLight = Brush.Parse("#D8BC85");
    public static readonly IBrush Red = Brush.Parse("#E3001B");
    public static readonly IBrush Blue = Brush.Parse("#3B9CFF");

    public static readonly FontFamily Head = new("avares://CDSTapeLab/Assets/Fonts#Vipnagorgialla");
    public static readonly FontFamily Mono = new("JetBrains Mono, Menlo, Consolas, Courier New");

    /// <summary>Besedilo v trenutnem jeziku (za nize, ki jih ni v Loc).</summary>
    public static string Tt(string sl, string en) => Loc.Lang == "sl" ? sl : en;

    public static TextBlock Label(string text = "", double size = 13, IBrush? brush = null) =>
        new() { Text = text, FontSize = size, Foreground = brush ?? Dim, VerticalAlignment = VerticalAlignment.Center };

    public static Border PanelBox(Control child, Thickness? pad = null) => new()
    {
        Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10), Padding = pad ?? new Thickness(18), Child = child,
    };
}

/// <summary>Gumb v slogu programa. Primarni = zlat.</summary>
public sealed class CdsButton : Border
{
    public event Action? Click;
    private readonly TextBlock _t = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly bool _primary;

    public CdsButton(bool primary = false)
    {
        _primary = primary;
        Child = _t;
        Cursor = new Cursor(StandardCursorType.Hand);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(14, 7);
        BorderThickness = new Thickness(1);
        Reset();
        if (primary) _t.FontWeight = FontWeight.SemiBold;
        PointerEntered += (_, _) => { if (IsEnabled) { if (_primary) Background = Ui.GoldLight; else BorderBrush = Ui.Gold; } };
        PointerExited += (_, _) => Reset();
        PointerReleased += (_, e) => { if (IsEnabled && e.InitialPressMouseButton == MouseButton.Left) Click?.Invoke(); };
    }

    private void Reset()
    {
        Background = _primary ? Ui.Gold : Ui.Panel;
        BorderBrush = _primary ? Ui.Gold : Ui.Line;
        _t.Foreground = _primary ? Ui.Bg : Ui.Text;
    }

    public string Text { get => _t.Text ?? ""; set => _t.Text = value; }

    public bool Enabled
    {
        get => IsEnabled;
        set { IsEnabled = value; Opacity = value ? 1.0 : 0.4; }
    }
}

/// <summary>Gumb za izbiro (segmentirano stikalo). Ekskluzivnost upravlja lastnik.</summary>
public sealed class Seg : Border
{
    public event Action? Click;
    private readonly TextBlock _t = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private bool _on;

    public Seg(string text = "")
    {
        _t.Text = text;
        Child = _t;
        Cursor = new Cursor(StandardCursorType.Hand);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(12, 6);
        BorderThickness = new Thickness(1);
        Margin = new Thickness(0, 0, 4, 4);
        Apply();
        PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Click?.Invoke(); };
    }

    public string Text { get => _t.Text ?? ""; set => _t.Text = value; }
    public bool On { get => _on; set { _on = value; Apply(); } }

    private void Apply()
    {
        Background = _on ? Ui.Gold : Ui.Panel;
        BorderBrush = _on ? Ui.Gold : Ui.Line;
        _t.Foreground = _on ? Ui.Bg : Ui.Text;
        _t.FontWeight = _on ? FontWeight.SemiBold : FontWeight.Normal;
    }
}
