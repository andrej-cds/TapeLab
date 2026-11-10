using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CDSMeasure.Audio;

namespace CDSMeasure.Views;

/// <summary>Začetni zaslon: izbira meritve. Meritve, ki jih ta izdaja še nima, so zatemnjene.</summary>
public sealed class StartView : UserControl, IDisposable
{
    public event Action<string>? Selected;

    private sealed class Tile
    {
        public string Id = "", TitleKey = "", DescKey = "", Icon = "";
        public bool Ready;
        public TextBlock Title = new(), Desc = new(), Soon = new();
    }

    private readonly List<Tile> _tiles = new();
    private readonly TextBlock _cardL = Ui.Label(), _cardV = Ui.Label("", 13, Ui.Text), _licL = Ui.Label(), _licV = Ui.Label("", 13, Ui.GoldLight);

    public StartView()
    {
        _tiles.Add(new Tile { Id = "wfs", TitleKey = "tile.wfs.t", DescKey = "tile.wfs.d", Ready = true, Icon = "M0,20 C8,-6 22,-6 30,20 S52,46 60,20 S82,-6 90,20" });
        _tiles.Add(new Tile { Id = "pb", TitleKey = "tile.pb.t", DescKey = "tile.pb.d", Ready = true, Icon = "M0,38 L10,36 L20,16 L34,12 L60,12 L80,14 L92,24 L100,46" });
        _tiles.Add(new Tile { Id = "rec", TitleKey = "tile.rec.t", DescKey = "tile.rec.d", Ready = true, Icon = "M0,40 L10,38 L20,20 L34,14 L60,14 L80,12 L92,20 L100,40" });
        _tiles.Add(new Tile { Id = "az", TitleKey = "tile.az.t", DescKey = "tile.az.d", Ready = true, Icon = "M0,30 L100,30 M8,44 L92,16" });
        _tiles.Add(new Tile { Id = "volt", TitleKey = "tile.volt.t", DescKey = "tile.volt.d", Ready = true,
            Icon = "M0,36 L100,36 M0,36 L0,24 M25,36 L25,28 M50,36 L50,24 M75,36 L75,28 M100,36 L100,24 M34,6 L52,36" });

        var root = new Grid { Margin = new Thickness(40, 34) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var grid = new UniformGrid { Rows = 1, Columns = _tiles.Count, VerticalAlignment = VerticalAlignment.Center, Height = 330 };
        foreach (var t in _tiles)
        {
            var stack = new StackPanel();
            stack.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(t.Icon), Stroke = Ui.Gold, StrokeThickness = 3.5, Width = 96, Height = 48,
                Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 26),
            });
            t.Title.FontFamily = Ui.Head; t.Title.FontSize = 19; t.Title.Foreground = Ui.Text; t.Title.TextWrapping = TextWrapping.Wrap;
            t.Desc.FontSize = 14; t.Desc.Foreground = Ui.Dim; t.Desc.TextWrapping = TextWrapping.Wrap; t.Desc.Margin = new Thickness(0, 10, 0, 0);
            t.Soon.FontSize = 12; t.Soon.TextWrapping = TextWrapping.Wrap; t.Soon.Foreground = Ui.Gold; t.Soon.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(t.Title); stack.Children.Add(t.Desc);
            if (!t.Ready) stack.Children.Add(t.Soon);

            var box = new Border
            {
                Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22), Margin = new Thickness(6, 0), Child = stack,
                Cursor = t.Ready ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
                Opacity = t.Ready ? 1.0 : 0.55,
            };
            if (t.Ready)
            {
                box.PointerEntered += (_, _) => box.BorderBrush = Ui.Gold;
                box.PointerExited += (_, _) => box.BorderBrush = Ui.Line;
                var id = t.Id;
                box.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Selected?.Invoke(id); };
            }
            grid.Children.Add(box);
        }
        Grid.SetRow(grid, 0); root.Children.Add(grid);

        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(_cardL); left.Children.Add(_cardV);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(_licL); right.Children.Add(_licV);
        Grid.SetColumn(left, 0); Grid.SetColumn(right, 1);
        bottom.Children.Add(left); bottom.Children.Add(right);
        var panel = Ui.PanelBox(bottom, new Thickness(18, 12));
        Grid.SetRow(panel, 1); root.Children.Add(panel);

        Content = root;
        Loc.Changed += ApplyLang;
        ApplyLang();
    }

    private void ApplyLang()
    {
        foreach (var t in _tiles)
        {
            t.Title.Text = Loc.T(t.TitleKey);
            t.Desc.Text = Loc.T(t.DescKey);
            t.Soon.Text = Ui.Tt(OperatingSystem.IsMacOS() ? "Kmalu v različici za Mac" : "Kmalu v različici za Linux",
                                OperatingSystem.IsMacOS() ? "Coming soon in the Mac version" : "Coming soon in the Linux version");
        }
        _cardL.Text = Loc.T("card");
        _cardV.Text = PaEngine.CardText();
        _licL.Text = Loc.T("lic");
        _licV.Text = "ALPHA";
    }

    public void Dispose() => Loc.Changed -= ApplyLang;
}
