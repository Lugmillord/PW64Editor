using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PW64Editor.App.Services;
using PW64Editor.Core.Map;
using PW64Editor.Core.Map.Rendering;
using PW64Editor.Core.Missions;

namespace PW64Editor.App.Views;

/// <summary>The map picture, the mouse and the camera of the Map tab.</summary>
public partial class MapTab
{
    private readonly MapRenderer _renderer = new();
    private readonly MapCamera _camera = new();
    private readonly MapCameraController _controller;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private WriteableBitmap? _bitmap;
    private MapImage? _image;
    private int _selectedPick = -1;
    private bool _renderPending;
    private int _interactive;
    private int _renderScale = 1;

    // The drag on the map: which action, where it was last.
    private MapMouseAction _dragAction;
    private Point _dragLast;
    private Point _dragStart;
    private bool _dragMoved;

    private AxisRow[] _cameraPosition = [];
    private AxisRow[] _cameraRotation = [];
    private AxisRow[] _objectPosition = [];
    private AxisRow[] _objectRotation = [];

    private static MapMouseSettings MouseSettings => EditorContext.Settings.MapMouse;

    private void SetUpView()
    {
        _controller.Orbit = false;
        _renderTimer.Tick += (_, _) =>
        {
            MoveByKeys();
            if (_renderPending)
            {
                _renderPending = false;
                RenderNow();
            }
        };
        _renderTimer.Start();
        KeyDown += OnKey;

        _filling = true;
        ViewModeBox.ItemsSource = new[] { L.T("Normal (textures)"), L.T("White, without textures"), L.T("Wireframe") };
        ViewModeBox.SelectedIndex = 0;
        LayerBox.ItemsSource = new[] { L.T("Island"), L.T("Overview map"), L.T("Island and overview map") };
        LayerBox.SelectedIndex = 0;

        MouseModeBox.ItemsSource = new[] { L.T("Select and move"), L.T("User defined") };
        string[] actions = [L.T("Nothing"), L.T("Select"), L.T("Pan the view"), L.T("Rotate the view"), L.T("Move the object"), L.T("Rotate the object")];
        LeftClickBox.ItemsSource = actions;
        MiddleClickBox.ItemsSource = actions;
        RightClickBox.ItemsSource = actions;
        WheelBox.ItemsSource = new[] { L.T("Zoom"), L.T("Forward and back"), L.T("Height of the object") };
        MapMouseSettings mouse = MouseSettings;
        MouseModeBox.SelectedIndex = mouse.Mode == MapMouseMode.Custom ? 1 : 0;
        CameraSpeedSlider.Value = Math.Clamp(mouse.CameraSpeed, 0.1, 5);
        ObjectSpeedSlider.Value = Math.Clamp(mouse.ObjectSpeed, 0.1, 5);
        _filling = false;
        _controller.Speed = (float)CameraSpeedSlider.Value;
        ShowMouseSettings();

        foreach ((AxisView view, string label) in new[]
        {
            (AxisView.Top, L.T("Top")), (AxisView.North, L.T("North")), (AxisView.South, L.T("South")),
            (AxisView.West, L.T("West")), (AxisView.East, L.T("East")),
        })
        {
            AxisViewPanel.Children.Add(ToolButton(label, null, () => _controller.AxisView(view)));
        }

        AxisViewPanel.Children.Add(ToolButton(L.T("3D"), L.T("Back to the perspective view, looking at the same place"), _controller.LeaveAxisView));
        CameraButtonPanel.Children.Add(ToolButton(L.T("Start view"), L.T("The view after loading: the whole island from the south at an angle"),
            () => _controller.ShowAll(_scene?.Bounds ?? Box3.Empty)));
        CameraButtonPanel.Children.Add(ToolButton(L.T("Show all"), L.T("The whole island from the current direction"),
            () => _controller.FitAll(_scene?.Bounds ?? Box3.Empty)));
        CameraButtonPanel.Children.Add(ToolButton(L.T("Focus"), L.T("Looks at the chosen object from the current direction"), FocusSelected));

        BuildCameraTools();
        BuildObjectTools();
        UpdateLayerControls();
        UpdateCameraRows();
        UpdateTransformRows();
    }

    private Button ToolButton(string label, string? tip, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 3, 3), Padding = new Thickness(5, 1, 5, 1), ToolTip = tip };
        button.Click += (_, _) =>
        {
            if (_scene is not null)
            {
                action();
                RequestRender();
            }
        };
        return button;
    }

    /// <summary>Looks at the chosen object (keeps the direction of the view).</summary>
    private void FocusSelected()
    {
        if (_scene is null || _selectedPick < 0)
        {
            return;
        }

        Box3 box = _scene.BoundsOf(_selectedPick);
        if (box.IsEmpty && SelectedPosition is { } p)
        {
            box = new Box3(p - new Vector3(5), p + new Vector3(5));
        }

        _controller.Focus(box);
    }

    // ----------------------------------------------------------------- drawing

    /// <summary>Draws the map again soon (several requests in a row make one drawing).</summary>
    private void RequestRender() => _renderPending = true;

    private MapRenderSettings Settings => new()
    {
        ViewMode = (MapViewMode)Math.Max(0, ViewModeBox.SelectedIndex),
        Layer = (MapLayerMode)Math.Max(0, LayerBox.SelectedIndex),
        MapOpacity = 1 - (float)(OpacitySlider.Value / 100),
        ShowTiles = TilesBox.IsChecked == true,
        Selected = _selectedPick,
        SelectedPosition = SelectedPosition,
        Gizmo = RotateModeButton.IsChecked == true ? GizmoMode.Rotate : GizmoMode.Move,
        GizmoRotation = ObjectAxesButton.IsChecked == true ? SelectedRotation : null,
        HideMissionObjects = false,
        ShowInvisible = InvisibleBox.IsChecked == true,
        Bilinear = BilinearBox.IsChecked == true,
        VertexColors = VertexColorBox.IsChecked == true,
        Lighting = LightingBox.IsChecked == true,
        AntiAlias = AntiAliasBox.IsChecked == true && _interactive == 0,
    };

    private void RenderNow()
    {
        if (_scene is null || MapHost.ActualWidth < 2 || MapHost.ActualHeight < 2)
        {
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        _renderScale = _interactive > 0 ? 2 : 1;
        int width = Math.Max(1, (int)(MapHost.ActualWidth * dpi.DpiScaleX) / _renderScale);
        int height = Math.Max(1, (int)(MapHost.ActualHeight * dpi.DpiScaleY) / _renderScale);
        _image = _renderer.Render(_scene, _camera, Settings, width, height);
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            MapImage.Source = _bitmap;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _image.Pixels, width * 4, 0);
        UpdateCameraRows();
    }

    private void OnMapSizeChanged(object sender, SizeChangedEventArgs e) => RequestRender();

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (!_filling)
        {
            UpdateLayerControls();
            if (_scene is not null && (ReferenceEquals(sender, LayerBox) || ReferenceEquals(sender, InvisibleBox)))
            {
                // The list shows only what the map shows.
                int selected = _selectedPick;
                FillObjectList();
                Select(_rows.Any(r => r.Pick == selected) ? selected : -1, fromList: false);
            }

            RequestRender();
        }
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => RequestRender();

    private void UpdateLayerControls()
    {
        bool both = LayerBox.SelectedIndex == (int)MapLayerMode.Both;
        OpacityLabel.IsEnabled = both;
        OpacitySlider.IsEnabled = both;
    }

    private void OnGizmoModeChanged(object sender, RoutedEventArgs e) => RequestRender();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        if (e.Key == Key.Q)
        {
            MoveModeButton.IsChecked = true;
            RequestRender();
            e.Handled = true;
        }
        else if (e.Key == Key.E)
        {
            RotateModeButton.IsChecked = true;
            RequestRender();
            e.Handled = true;
        }
    }

    // ----------------------------------------------------------------- keys W, A, S, D

    private readonly System.Diagnostics.Stopwatch _keyClock = new();
    private bool _keyMoving;

    /// <summary>
    /// Moves the view while W, A, S or D is held and the map has the keyboard focus (after a click into the map): in the
    /// axis views up/left/down/right in the picture (the compass directions from above), in the perspective forward/back
    /// and sideways at the same height (orbit mode: around the chosen object).
    /// </summary>
    private void MoveByKeys()
    {
        bool active = _scene is not null && ReferenceEquals(Keyboard.FocusedElement, this) && (Keyboard.Modifiers & ModifierKeys.Control) == 0;
        float forward = 0, right = 0;
        if (active)
        {
            forward = (Keyboard.IsKeyDown(Key.W) ? 1 : 0) - (Keyboard.IsKeyDown(Key.S) ? 1 : 0);
            right = (Keyboard.IsKeyDown(Key.D) ? 1 : 0) - (Keyboard.IsKeyDown(Key.A) ? 1 : 0);
        }

        if (forward == 0 && right == 0)
        {
            if (_keyMoving)
            {
                _keyMoving = false;
                _keyClock.Reset();
                EndInteraction();
            }

            return;
        }

        if (!_keyMoving)
        {
            _keyMoving = true;
            _keyClock.Restart();
            StartInteraction();
            return;
        }

        float seconds = (float)Math.Min(0.1, _keyClock.Elapsed.TotalSeconds);
        _keyClock.Restart();
        _controller.MoveByKeys(forward, right, seconds);
        RequestRender();
    }

    // ----------------------------------------------------------------- mouse on the map

    /// <summary>The picture pixel under a point of the map (the picture may be drawn at half resolution).</summary>
    private (int X, int Y) PixelAt(Point p)
    {
        if (_image is null || MapHost.ActualWidth <= 0 || MapHost.ActualHeight <= 0)
        {
            return (-1, -1);
        }

        return ((int)(p.X / MapHost.ActualWidth * _image.Width), (int)(p.Y / MapHost.ActualHeight * _image.Height));
    }

    private MapMouseAction ActionOf(MouseButton button)
    {
        var effective = MouseSettings.Effective;
        return button switch
        {
            MouseButton.Left => effective.Left,
            MouseButton.Middle => effective.Middle,
            MouseButton.Right => effective.Right,
            _ => MapMouseAction.None,
        };
    }

    private void OnMapMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (_scene is null || _dragAction != MapMouseAction.None)
        {
            return;
        }

        MapMouseAction action = ActionOf(e.ChangedButton);
        if (action == MapMouseAction.None)
        {
            return;
        }

        _dragAction = action;
        _dragStart = _dragLast = e.GetPosition(MapHost);
        _dragMoved = false;
        MapHost.CaptureMouse();
        e.Handled = true;
    }

    private void OnMapMouseMove(object sender, MouseEventArgs e)
    {
        Point p = e.GetPosition(MapHost);
        ShowPointUnderMouse(p);
        if (_dragAction == MapMouseAction.None)
        {
            return;
        }

        double dx = p.X - _dragLast.X, dy = p.Y - _dragLast.Y;
        _dragLast = p;
        if (!_dragMoved && Math.Abs(p.X - _dragStart.X) + Math.Abs(p.Y - _dragStart.Y) < 4)
        {
            return;
        }

        if (!_dragMoved)
        {
            _dragMoved = true;
            _interactive++;
        }

        int height = (int)Math.Max(1, MapHost.ActualHeight);
        switch (_dragAction)
        {
            case MapMouseAction.Pan:
            case MapMouseAction.Select: // a click chooses, a drag pans
                _controller.Pan((float)dx, (float)dy, height);
                break;
            case MapMouseAction.Rotate:
                _controller.Rotate((float)dx, (float)dy);
                break;
        }

        RequestRender();
    }

    private void OnMapMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragAction == MapMouseAction.None || ActionOf(e.ChangedButton) != _dragAction)
        {
            return;
        }

        MapHost.ReleaseMouseCapture();
        if (!_dragMoved && _dragAction == MapMouseAction.Select && _image is not null)
        {
            (int x, int y) = PixelAt(e.GetPosition(MapHost));
            Select(_image.PickAt(x, y, 4 / _renderScale + 1), fromList: false);
        }

        if (_dragMoved)
        {
            _interactive = Math.Max(0, _interactive - 1);
        }

        _dragAction = MapMouseAction.None;
        RequestRender();
    }

    private void OnMapMouseWheel(object sender, MouseWheelEventArgs e)
    {
        float steps = e.Delta / 120f;
        switch (MouseSettings.Effective.Wheel)
        {
            case MapWheelAction.Zoom:
                // Towards the point under the mouse (it stays under the mouse).
                _controller.ZoomToward(steps, PointUnderMouse(e.GetPosition(MapHost)));
                break;
            case MapWheelAction.ForwardBack:
                _controller.ForwardBack(steps * 40, (int)Math.Max(1, MapHost.ActualHeight));
                break;
        }

        RequestRender();
        e.Handled = true;
    }

    /// <summary>The point of the map under the mouse: the drawn surface there, else the sea level along the view ray.</summary>
    private Vector3 PointUnderMouse(Point p)
    {
        if (SurfaceAt(p) is { } surface)
        {
            return surface;
        }

        double width = Math.Max(1, MapHost.ActualWidth), height = Math.Max(1, MapHost.ActualHeight);
        (Vector3 origin, Vector3 direction) = _camera.Ray((float)p.X, (float)p.Y, (int)width, (int)height);
        return _controller.PointAlong(origin, direction);
    }

    /// <summary>The drawn surface under a point of the map (null where only the sky is).</summary>
    private Vector3? SurfaceAt(Point p)
    {
        if (_image is null)
        {
            return null;
        }

        (int x, int y) = PixelAt(p);
        if (x < 0 || y < 0 || x >= _image.Width || y >= _image.Height)
        {
            return null;
        }

        float depth = _image.Depth[y * _image.Width + x];
        if (depth >= 1)
        {
            return null;
        }

        Matrix4x4 viewProjection = _camera.View * _camera.Projection((float)_image.Width / _image.Height);
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse))
        {
            return null;
        }

        var ndc = new Vector4(2f * (x + 0.5f) / _image.Width - 1, 1 - 2f * (y + 0.5f) / _image.Height, depth, 1);
        Vector4 world = Vector4.Transform(ndc, inverse);
        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }

    /// <summary>Shows where the mouse points on the map (game meters) and what is there.</summary>
    private void ShowPointUnderMouse(Point p)
    {
        if (_image is null || _scene is null)
        {
            return;
        }

        if (SurfaceAt(p) is not { } point)
        {
            MapStatus.Text = string.Empty;
            return;
        }

        (int x, int y) = PixelAt(p);
        int pick = _image.Picks[y * _image.Width + x];
        string thing = pick >= 0 && pick < _scene.Picks.Count && _rows.FirstOrDefault(r => r.Pick == pick) is { } row ? "  ·  " + row.Name : string.Empty;
        MapStatus.Text = L.F("X {0:0.#} m, Y {1:0.#} m, Z {2:0.#} m", point.X * GameUnits.MetersPerUnit, point.Y * GameUnits.MetersPerUnit,
            point.Z * GameUnits.MetersPerUnit) + thing;
    }

    // ----------------------------------------------------------------- mouse settings

    private void OnMouseSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling)
        {
            return;
        }

        MapMouseSettings mouse = MouseSettings;
        mouse.Mode = MouseModeBox.SelectedIndex == 1 ? MapMouseMode.Custom : MapMouseMode.SelectAndMove;
        if (mouse.Mode == MapMouseMode.Custom)
        {
            mouse.Left = (MapMouseAction)Math.Max(0, LeftClickBox.SelectedIndex);
            mouse.Middle = (MapMouseAction)Math.Max(0, MiddleClickBox.SelectedIndex);
            mouse.Right = (MapMouseAction)Math.Max(0, RightClickBox.SelectedIndex);
            mouse.Wheel = (MapWheelAction)Math.Max(0, WheelBox.SelectedIndex);
        }

        EditorContext.SaveSettings();
        ShowMouseSettings();
    }

    /// <summary>Shows what the buttons do; only the user-defined mode lets them be chosen.</summary>
    private void ShowMouseSettings()
    {
        _filling = true;
        var effective = MouseSettings.Effective;
        LeftClickBox.SelectedIndex = (int)effective.Left;
        MiddleClickBox.SelectedIndex = (int)effective.Middle;
        RightClickBox.SelectedIndex = (int)effective.Right;
        WheelBox.SelectedIndex = (int)effective.Wheel;
        bool custom = MouseSettings.Mode == MapMouseMode.Custom;
        LeftClickBox.IsEnabled = custom;
        MiddleClickBox.IsEnabled = custom;
        RightClickBox.IsEnabled = custom;
        WheelBox.IsEnabled = custom;
        _filling = false;
    }

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_filling || CameraSpeedSlider is null || ObjectSpeedSlider is null)
        {
            return;
        }

        MouseSettings.CameraSpeed = CameraSpeedSlider.Value;
        MouseSettings.ObjectSpeed = ObjectSpeedSlider.Value;
        _controller.Speed = (float)CameraSpeedSlider.Value;
        EditorContext.SaveSettings();
    }

    // ----------------------------------------------------------------- camera tools

    private void OnCameraModeChanged(object sender, RoutedEventArgs e)
    {
        _controller.Orbit = OrbitButton.IsChecked == true;
        if (_controller.Orbit && SelectedPosition is { } target)
        {
            _controller.LookAt(target, Math.Max(80, Vector3.Distance(_camera.Position, target)));
            RequestRender();
        }
    }

    private void StartInteraction() => _interactive++;

    private void EndInteraction()
    {
        _interactive = Math.Max(0, _interactive - 1);
        RequestRender();
    }

    private void BuildCameraTools()
    {
        var pad = new DragPad();
        var forward = new DragBar(Orientation.Vertical, 84) { Margin = new Thickness(6, 0, 0, 0), ToolTip = L.T("Forward and back") };
        var zoom = new DragBar(Orientation.Horizontal, 84) { ToolTip = L.T("Zoom") };
        pad.ToolTip = L.T("Drag to move the view");
        pad.Dragged += (dx, dy) =>
        {
            _controller.Pan((float)dx, (float)dy, (int)Math.Max(1, MapHost.ActualHeight));
            RequestRender();
        };
        forward.Dragged += (_, dy) =>
        {
            _controller.ForwardBack((float)-dy * 3, (int)Math.Max(1, MapHost.ActualHeight));
            RequestRender();
        };
        zoom.Dragged += (dx, _) =>
        {
            _controller.ZoomBy((float)dx * 2);
            RequestRender();
        };
        foreach (DragField field in new DragField[] { pad, forward, zoom })
        {
            field.DragStarted += StartInteraction;
            field.DragEnded += EndInteraction;
        }

        Place(CameraPanGrid, pad, 0, 0);
        Place(CameraPanGrid, forward, 0, 1);
        Place(CameraPanGrid, new TextBlock { Text = L.T("Zoom"), Margin = new Thickness(0, 4, 0, 1) }, 1, 0);
        Place(CameraPanGrid, zoom, 2, 0);

        var rotate = new DragPad { ToolTip = L.T("Drag to turn the view") };
        rotate.Dragged += (dx, dy) =>
        {
            _controller.Rotate((float)dx, (float)dy);
            RequestRender();
        };
        rotate.DragStarted += StartInteraction;
        rotate.DragEnded += EndInteraction;
        Place(CameraRotateGrid, rotate, 0, 0);

        _cameraPosition = AxisRows(CameraPositionPanel);
        for (int axis = 0; axis < 3; axis++)
        {
            int a = axis;
            AxisRow row = _cameraPosition[axis];
            row.Edited += value => SetCameraPosition(a, (float)(value / GameUnits.MetersPerUnit));
            row.Stepped += step => SetCameraPosition(a, Component(_camera.Position, a) + step * 10f * (float)CameraSpeedSlider.Value / GameUnits.MetersPerUnit);
            row.Dragged += dx => SetCameraPosition(a, Component(_camera.Position, a) + (float)dx * 2f * (float)CameraSpeedSlider.Value);
            row.DragStarted += StartInteraction;
            row.DragEnded += EndInteraction;
        }

        // Rotation rows: about X (pitch), Y (roll), Z (heading).
        _cameraRotation = AxisRows(CameraRotationPanel);
        for (int axis = 0; axis < 3; axis++)
        {
            int a = axis;
            AxisRow row = _cameraRotation[axis];
            row.Edited += value => SetCameraAngle(a, (float)value);
            row.Stepped += step => SetCameraAngle(a, CameraAngle(a) + step * 5f);
            row.Dragged += dx => SetCameraAngle(a, CameraAngle(a) + (float)dx * 0.5f * (float)CameraSpeedSlider.Value);
            row.DragStarted += StartInteraction;
            row.DragEnded += EndInteraction;
        }
    }

    private void BuildObjectTools()
    {
        var pad = new DragPad { ToolTip = L.T("Drag to move the object") };
        var height = new DragBar(Orientation.Vertical, 84) { Margin = new Thickness(6, 0, 0, 0), ToolTip = L.T("Height of the object") };
        Place(ObjectMoveGrid, pad, 0, 0);
        Place(ObjectMoveGrid, height, 0, 1);
        Place(ObjectRotateGrid, new DragPad { ToolTip = L.T("Drag to turn the object") }, 0, 0);
        _objectPosition = AxisRows(ObjectPositionPanel);
        _objectRotation = AxisRows(ObjectRotationPanel);

        // Moving and turning objects come with the next part of the map editor; the tools show the values meanwhile.
        foreach (UIElement element in new UIElement[] { ObjectMoveGrid, ObjectRotateGrid, ObjectPositionPanel, ObjectRotationPanel, GroundButton, ObjectSpeedSlider })
        {
            element.IsEnabled = false;
        }
    }

    private static AxisRow[] AxisRows(Panel panel)
    {
        var rows = new[] { new AxisRow("X"), new AxisRow("Y"), new AxisRow("Z") };
        foreach (AxisRow row in rows)
        {
            panel.Children.Add(row);
        }

        return rows;
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        while (grid.RowDefinitions.Count <= row)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        while (grid.ColumnDefinitions.Count <= column)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static Vector3 WithComponent(Vector3 v, int axis, float value) => axis switch
    {
        0 => v with { X = value },
        1 => v with { Y = value },
        _ => v with { Z = value },
    };

    private void SetCameraPosition(int axis, float units)
    {
        _camera.Position = WithComponent(_camera.Position, axis, units);
        RequestRender();
    }

    /// <summary>The camera's angle about an axis: X = pitch, Y = roll, Z = heading.</summary>
    private float CameraAngle(int axis) => axis switch { 0 => _camera.Pitch, 1 => _camera.Roll, _ => _camera.Heading };

    private void SetCameraAngle(int axis, float degrees)
    {
        _camera.Orthographic = false;
        switch (axis)
        {
            case 0:
                _camera.Pitch = Math.Clamp(degrees, -89.5f, 89.5f);
                break;
            case 1:
                _camera.Roll = Math.Clamp(degrees, -180, 180);
                break;
            default:
                _camera.Heading = degrees;
                break;
        }

        RequestRender();
    }

    private void UpdateCameraRows()
    {
        for (int axis = 0; axis < 3 && axis < _cameraPosition.Length; axis++)
        {
            _cameraPosition[axis].Show(Component(_camera.Position, axis) * GameUnits.MetersPerUnit);
            _cameraRotation[axis].Show(CameraAngle(axis));
        }
    }

    /// <summary>Shows the position and rotation of the chosen object in the transform tools.</summary>
    private void UpdateTransformRows()
    {
        Vector3? position = SelectedPosition;
        Vector3? angles = SelectedAngles;
        for (int axis = 0; axis < 3 && axis < _objectPosition.Length; axis++)
        {
            if (position is { } p)
            {
                _objectPosition[axis].Show(Component(p, axis) * GameUnits.MetersPerUnit);
            }
            else
            {
                _objectPosition[axis].Clear();
            }

            // Rows: about X = pitch (game angle 2), Y = roll (3), Z = heading (1).
            if (angles is { } a)
            {
                _objectRotation[axis].Show(axis switch { 0 => a.Y, 1 => a.Z, _ => a.X });
            }
            else
            {
                _objectRotation[axis].Clear();
            }
        }
    }
}
