using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PW64Editor.App.Views;

/// <summary>
/// A field to drag in: pressing the left button in it starts a drag, and the drag goes on outside the field until the
/// button is released (the field is only where the drag starts). Reports the mouse movement in pixels.
/// </summary>
public class DragField : Border
{
    private Point _last;
    private bool _dragging;

    /// <summary>Creates the field.</summary>
    public DragField()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xE4, 0xE6, 0xE9));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x55, 0x5C));
        BorderThickness = new Thickness(1.5);
        Cursor = Cursors.SizeAll;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) => EndDrag();
    }

    /// <summary>The mouse moved by (dx, dy) pixels during a drag.</summary>
    public event Action<double, double>? Dragged;

    /// <summary>A drag starts.</summary>
    public event Action? DragStarted;

    /// <summary>A drag ends.</summary>
    public event Action? DragEnded;

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        _last = e.GetPosition(this);
        _dragging = CaptureMouse();
        if (_dragging)
        {
            DragStarted?.Invoke();
        }

        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point p = e.GetPosition(this);
        double dx = p.X - _last.X, dy = p.Y - _last.Y;
        _last = p;
        if (dx != 0 || dy != 0)
        {
            Dragged?.Invoke(dx, dy);
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        EndDrag();
    }

    private void EndDrag()
    {
        if (_dragging)
        {
            _dragging = false;
            DragEnded?.Invoke();
        }
    }
}

/// <summary>A square 2D drag field with two arrows (right and up).</summary>
public sealed class DragPad : DragField
{
    /// <summary>Creates the pad.</summary>
    public DragPad()
    {
        Width = 84;
        Height = 84;
        var canvas = new Canvas { IsHitTestVisible = false };
        Brush ink = new SolidColorBrush(Color.FromRgb(0x30, 0x34, 0x3A));
        void Line(double x1, double y1, double x2, double y2) =>
            canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = ink, StrokeThickness = 3 });
        Line(26, 58, 26, 20);
        Line(26, 20, 20, 28);
        Line(26, 20, 32, 28);
        Line(26, 58, 64, 58);
        Line(64, 58, 56, 52);
        Line(64, 58, 56, 64);
        Child = canvas;
    }
}

/// <summary>A narrow 1D drag field, horizontal or vertical, with grip lines.</summary>
public sealed class DragBar : DragField
{
    /// <summary>Creates the bar.</summary>
    public DragBar(Orientation orientation, double length)
    {
        Orientation = orientation;
        bool horizontal = orientation == Orientation.Horizontal;
        Width = horizontal ? length : 16;
        Height = horizontal ? 16 : length;
        Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS;
        var canvas = new Canvas { IsHitTestVisible = false };
        Brush ink = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x98));
        for (double p = 5; p < length - 4; p += 5)
        {
            canvas.Children.Add(horizontal
                ? new Line { X1 = p, Y1 = 3, X2 = p, Y2 = 11, Stroke = ink, StrokeThickness = 1 }
                : new Line { X1 = 3, Y1 = p, X2 = 11, Y2 = p, Stroke = ink, StrokeThickness = 1 });
        }

        Child = canvas;
    }

    /// <summary>Horizontal or vertical.</summary>
    public Orientation Orientation { get; }
}

/// <summary>
/// One axis row: the axis name, the number (typed, or stepped with the up/down buttons) and a 1D drag field.
/// </summary>
public sealed class AxisRow : Grid
{
    private readonly TextBox _box;
    private readonly DragBar _bar;
    private bool _setting;

    /// <summary>Creates the row.</summary>
    /// <param name="axis">The axis name ("X", "Y", "Z").</param>
    public AxisRow(string axis)
    {
        Margin = new Thickness(0, 1, 0, 1);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock { Text = axis, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _box = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, Height = 20 };
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        _box.LostKeyboardFocus += (_, _) => Commit();

        var steps = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(1, 0, 0, 0) };
        RepeatButton up = StepButton("▲"), down = StepButton("▼");
        up.Click += (_, _) => Stepped?.Invoke(1);
        down.Click += (_, _) => Stepped?.Invoke(-1);
        steps.Children.Add(up);
        steps.Children.Add(down);

        _bar = new DragBar(Orientation.Horizontal, 92) { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _bar.Dragged += (dx, _) => Dragged?.Invoke(dx);
        _bar.DragStarted += () => DragStarted?.Invoke();
        _bar.DragEnded += () => DragEnded?.Invoke();

        SetColumn(label, 0);
        SetColumn(_box, 1);
        SetColumn(steps, 2);
        SetColumn(_bar, 3);
        Children.Add(label);
        Children.Add(_box);
        Children.Add(steps);
        Children.Add(_bar);
    }

    /// <summary>A number was typed (in the shown unit).</summary>
    public event Action<double>? Edited;

    /// <summary>An up (+1) or down (−1) button was pressed.</summary>
    public event Action<int>? Stepped;

    /// <summary>The drag field moved by this many pixels.</summary>
    public event Action<double>? Dragged;

    /// <summary>A drag of the field starts.</summary>
    public event Action? DragStarted;

    /// <summary>A drag of the field ends.</summary>
    public event Action? DragEnded;

    /// <summary>Shows a value (unless the user is typing in the box).</summary>
    public void Show(double value)
    {
        if (_box.IsKeyboardFocusWithin)
        {
            return;
        }

        _setting = true;
        _box.Text = value.ToString("0.##", CultureInfo.CurrentCulture);
        _setting = false;
    }

    /// <summary>Shows nothing.</summary>
    public void Clear() => _box.Text = string.Empty;

    private void Commit()
    {
        if (_setting || !IsEnabled)
        {
            return;
        }

        if (double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            || double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            Edited?.Invoke(value);
        }
    }

    private static RepeatButton StepButton(string glyph) => new()
    {
        Content = new TextBlock { Text = glyph, FontSize = 6, HorizontalAlignment = HorizontalAlignment.Center },
        Height = 10,
        Width = 13,
        Padding = new Thickness(0),
        Delay = 400,
        Interval = 60,
    };
}
