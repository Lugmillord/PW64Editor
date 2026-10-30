using System.Numerics;

namespace PW64Editor.Core.Map.Rendering;

/// <summary>What a mouse button does on the map.</summary>
public enum MapMouseAction
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>A click chooses the object under the mouse; holding the button and dragging pans the view.</summary>
    Select,

    /// <summary>Moves the view sideways and up/down.</summary>
    Pan,

    /// <summary>Turns the view (orbit: around the chosen object).</summary>
    Rotate,

    /// <summary>Moves the chosen object.</summary>
    MoveObject,

    /// <summary>Turns the chosen object.</summary>
    RotateObject,
}

/// <summary>What the mouse wheel does on the map.</summary>
public enum MapWheelAction
{
    /// <summary>Moves the camera towards what it looks at (or changes the visible area of an axis view).</summary>
    Zoom,

    /// <summary>Moves the camera forward and back over the map, at the same height.</summary>
    ForwardBack,

    /// <summary>Raises and lowers the chosen object.</summary>
    ObjectHeight,
}

/// <summary>The three mouse modes of the Map tab.</summary>
public enum MapMouseMode
{
    /// <summary>Kept for settings saved by earlier versions; works like <see cref="SelectAndMove"/>.</summary>
    Navigate,

    /// <summary>Choosing and moving: a left click chooses, holding the left button pans (later: drags the chosen object at its handles), the right button turns the view (orbit mode: around the object), the middle button pans, the wheel zooms.</summary>
    SelectAndMove,

    /// <summary>Every button as the user sets it.</summary>
    Custom,
}

/// <summary>The mouse settings of the Map tab (kept per user, not per project).</summary>
public sealed class MapMouseSettings
{
    /// <summary>The mode.</summary>
    public MapMouseMode Mode { get; set; } = MapMouseMode.SelectAndMove;

    /// <summary>The left button in the custom mode.</summary>
    public MapMouseAction Left { get; set; } = MapMouseAction.Select;

    /// <summary>The middle button in the custom mode.</summary>
    public MapMouseAction Middle { get; set; } = MapMouseAction.Pan;

    /// <summary>The right button in the custom mode.</summary>
    public MapMouseAction Right { get; set; } = MapMouseAction.Rotate;

    /// <summary>The wheel in the custom mode.</summary>
    public MapWheelAction Wheel { get; set; } = MapWheelAction.Zoom;

    /// <summary>Speed of the camera tools (1 = normal).</summary>
    public double CameraSpeed { get; set; } = 1;

    /// <summary>Speed of the object tools (1 = normal).</summary>
    public double ObjectSpeed { get; set; } = 1;

    /// <summary>The actions in effect for (left, middle, right, wheel).</summary>
    public (MapMouseAction Left, MapMouseAction Middle, MapMouseAction Right, MapWheelAction Wheel) Effective => Mode switch
    {
        MapMouseMode.Navigate or MapMouseMode.SelectAndMove => (MapMouseAction.Select, MapMouseAction.Pan, MapMouseAction.Rotate, MapWheelAction.Zoom),
        _ => (Left, Middle, Right, Wheel),
    };
}

/// <summary>
/// Moves the map's camera: panning, turning, orbiting around a point, zooming, flying forward and back, and the axis
/// views. Distances follow what is seen (the farther the view, the bigger the steps).
/// </summary>
public sealed class MapCameraController
{
    /// <summary>Creates a controller for a camera.</summary>
    public MapCameraController(MapCamera camera) => Camera = camera;

    /// <summary>The camera.</summary>
    public MapCamera Camera { get; }

    /// <summary>True for the orbit mode: turning goes around <see cref="Target"/>.</summary>
    public bool Orbit { get; set; }

    /// <summary>The point the orbit goes around (the chosen object), or null for the point in the middle of the view (only used in orbit mode).</summary>
    public Vector3? Target { get; set; }

    /// <summary>Speed factor of all moves (the camera tools' speed slider).</summary>
    public float Speed { get; set; } = 1;

    /// <summary>The ground level used when nothing else is known (the sea).</summary>
    public float GroundLevel { get; set; }

    /// <summary>
    /// The point the camera looks at: in orbit mode the target, in an axis view the point in the middle of the picture (the
    /// camera stands 20000 units back), otherwise where the view's middle meets the ground, or 600 units ahead.
    /// </summary>
    public Vector3 FocusPoint()
    {
        if (Orbit && Target is { } target)
        {
            return target;
        }

        if (Camera.Orthographic)
        {
            return Camera.Position + Camera.Forward * 20000;
        }

        Vector3 forward = Camera.Forward;
        if (forward.Z < -0.05f)
        {
            float t = (Camera.Position.Z - GroundLevel) / -forward.Z;
            if (t > 0 && t < 20000)
            {
                return Camera.Position + forward * t;
            }
        }

        return Camera.Position + forward * 600;
    }

    private float Distance => Camera.Orthographic ? Camera.OrthographicHeight : MathF.Max(20, Vector3.Distance(Camera.Position, FocusPoint()));

    /// <summary>Moves the view by a drag of (dx, dy) pixels: the picture follows the mouse.</summary>
    public void Pan(float dx, float dy, int imageHeight)
    {
        float units = Camera.UnitsPerPixel(Distance, imageHeight) * Speed;
        Camera.Position += (-Camera.Right * dx + Camera.Up * dy) * units;
    }

    /// <summary>Turns the view by a drag of (dx, dy) pixels; in orbit mode around the focus point.</summary>
    public void Rotate(float dx, float dy)
    {
        float degrees = 0.3f * Speed;
        LeaveAxisView();

        if (Orbit)
        {
            Vector3 focus = FocusPoint();
            float distance = Vector3.Distance(Camera.Position, focus);
            Camera.Heading -= dx * degrees;
            Camera.Pitch = Math.Clamp(Camera.Pitch - dy * degrees, -89.5f, 89.5f);
            Camera.Position = focus - Camera.Forward * distance;
        }
        else
        {
            Camera.Heading -= dx * degrees;
            Camera.Pitch = Math.Clamp(Camera.Pitch - dy * degrees, -89.5f, 89.5f);
        }

        Camera.Heading = NormalizeAngle(Camera.Heading);
    }

    /// <summary>Zooms by wheel steps (positive = closer): towards the focus point, or a smaller visible area in an axis view.</summary>
    public void Zoom(float steps)
    {
        float factor = MathF.Pow(0.85f, steps * Speed);
        if (Camera.Orthographic)
        {
            Camera.OrthographicHeight = Math.Clamp(Camera.OrthographicHeight * factor, 10, 40000);
            return;
        }

        Vector3 focus = FocusPoint();
        Vector3 offset = Camera.Position - focus;
        float length = offset.Length();
        float target = Math.Clamp(length * factor, 3, 40000);
        Camera.Position = length > 0 ? focus + offset / length * target : Camera.Position - Camera.Forward * target;
    }

    /// <summary>
    /// Zooms by wheel steps (positive = closer) towards a point of the map (the point under the mouse): it stays under the
    /// mouse. In an axis view the visible area shrinks around it. In orbit mode the zoom goes to the chosen object.
    /// </summary>
    public void ZoomToward(float steps, Vector3 point)
    {
        if (Orbit && Target is not null)
        {
            Zoom(steps);
            return;
        }

        float factor = MathF.Pow(0.85f, steps * Speed);
        if (Camera.Orthographic)
        {
            float height = Math.Clamp(Camera.OrthographicHeight * factor, 10, 40000);
            float applied = height / Camera.OrthographicHeight;
            Vector3 delta = point - Camera.Position;
            Vector3 sideways = delta - Camera.Forward * Vector3.Dot(delta, Camera.Forward);
            Camera.Position += sideways * (1 - applied);
            Camera.OrthographicHeight = height;
            return;
        }

        Vector3 offset = Camera.Position - point;
        float length = offset.Length();
        if (length < 1e-3f)
        {
            return;
        }

        float target = Math.Clamp(length * factor, 3, 40000);
        Camera.Position = point + offset / length * target;
    }

    /// <summary>
    /// The point of the ground plane (the sea level) under a direction from the camera, or a point at the focus distance
    /// when the direction does not meet it (for zooming towards the sky).
    /// </summary>
    public Vector3 PointAlong(Vector3 origin, Vector3 direction)
    {
        if (direction.Z < -1e-4f)
        {
            float t = (origin.Z - GroundLevel) / -direction.Z;
            if (t > 0 && t < 40000)
            {
                return origin + direction * t;
            }
        }

        return origin + direction * Vector3.Distance(Camera.Position, FocusPoint());
    }

    /// <summary>Moves the camera forward (positive) or back over the map at the same height, by pixels of a drag or wheel.</summary>
    public void ForwardBack(float amount, int imageHeight)
    {
        Vector3 flat = Camera.Forward with { Z = 0 };
        if (flat.LengthSquared() < 1e-6f)
        {
            flat = Camera.Up with { Z = 0 };
        }

        flat = Vector3.Normalize(flat);
        Camera.Position += flat * amount * Camera.UnitsPerPixel(Distance, imageHeight) * Speed;
    }

    /// <summary>
    /// Moves by the keys W, A, S, D held for <paramref name="seconds"/> (<paramref name="forward"/>: W = 1, S = −1;
    /// <paramref name="right"/>: D = 1, A = −1). Axis views: up/down and left/right in the picture (from above: north,
    /// south, west, east). Perspective: forward/back and sideways at the same height, the direction stays. Orbit mode:
    /// around the focus point (W/S up and down, A/D left and right).
    /// </summary>
    public void MoveByKeys(float forward, float right, float seconds)
    {
        if (Camera.Orthographic)
        {
            float units = Camera.OrthographicHeight * 0.6f * seconds * Speed;
            Camera.Position += (Camera.Up * forward + Camera.Right * right) * units;
            return;
        }

        if (Orbit)
        {
            Vector3 focus = FocusPoint();
            float distance = Vector3.Distance(Camera.Position, focus);
            float degrees = 70 * seconds * Speed;
            Camera.Heading = NormalizeAngle(Camera.Heading + right * degrees);
            Camera.Pitch = Math.Clamp(Camera.Pitch - forward * degrees, -89.5f, 89.5f);
            Camera.Position = focus - Camera.Forward * distance;
            return;
        }

        Vector3 flat = Camera.Forward with { Z = 0 };
        if (flat.LengthSquared() < 1e-6f)
        {
            flat = Camera.Up with { Z = 0 };
        }

        flat = Vector3.Normalize(flat);
        Vector3 side = Vector3.Normalize(new Vector3(flat.Y, -flat.X, 0));
        float speed = MathF.Max(60, Distance * 0.8f) * seconds * Speed;
        Camera.Position += (flat * forward + side * right) * speed;
    }

    /// <summary>Changes the field of view by a drag (pixels to the right: narrower, i.e. closer).</summary>
    public void ZoomBy(float pixels)
    {
        if (Camera.Orthographic)
        {
            Camera.OrthographicHeight = Math.Clamp(Camera.OrthographicHeight * MathF.Pow(0.995f, pixels * Speed), 10, 40000);
        }
        else
        {
            Camera.FieldOfView = Math.Clamp(Camera.FieldOfView - pixels * 0.1f * Speed, 10, 100);
        }
    }

    /// <summary>
    /// Leaves an axis view: back to the perspective, looking at the same point from the same direction, from where the
    /// visible area is as big as before.
    /// </summary>
    public void LeaveAxisView()
    {
        if (!Camera.Orthographic)
        {
            return;
        }

        Vector3 focus = FocusPoint();
        float distance = Camera.OrthographicHeight / (2 * MathF.Tan(Camera.FieldOfView * MapMath.DegreesToRadians / 2));
        Camera.Orthographic = false;
        Camera.Position = focus - Camera.Forward * Math.Clamp(distance, 20, 40000);
    }

    /// <summary>Shows a whole box from the current direction (the axis view stays an axis view).</summary>
    public void FitAll(Box3 box)
    {
        if (box.IsEmpty)
        {
            return;
        }

        float size = MathF.Max(MathF.Max(box.Size.X, box.Size.Y), box.Size.Z);
        if (Camera.Orthographic)
        {
            Camera.OrthographicHeight = Math.Clamp(size * 1.1f, 10, 40000);
            Camera.Position = box.Center - Camera.Forward * 20000;
            return;
        }

        float distance = size * 0.5f / MathF.Tan(Camera.FieldOfView * MapMath.DegreesToRadians / 2) * 1.1f;
        Camera.Position = box.Center - Camera.Forward * Math.Clamp(distance, 20, 40000);
    }

    /// <summary>Looks at a box (the chosen object) from the current direction, close enough to see it well.</summary>
    public void Focus(Box3 box)
    {
        if (box.IsEmpty)
        {
            return;
        }

        float size = MathF.Max(box.Size.Length(), 10);
        if (Camera.Orthographic)
        {
            Camera.OrthographicHeight = Math.Clamp(size * 3, 10, 40000);
            Camera.Position = box.Center - Camera.Forward * 20000;
            return;
        }

        float distance = size * 1.5f / MathF.Tan(Camera.FieldOfView * MapMath.DegreesToRadians / 2);
        Camera.Position = box.Center - Camera.Forward * Math.Clamp(distance, 20, 40000);
    }

    /// <summary>Shows one of the axis views around the focus point.</summary>
    public void AxisView(AxisView view)
    {
        Vector3 focus = FocusPoint();
        float height = Camera.Orthographic ? Camera.OrthographicHeight : Distance * 2 * MathF.Tan(Camera.FieldOfView * MapMath.DegreesToRadians / 2);
        Camera.SetAxisView(view, focus, Math.Clamp(height, 50, 40000));
    }

    /// <summary>Shows a whole box from above at an angle (the start view of an island).</summary>
    public void ShowAll(Box3 box)
    {
        Camera.Orthographic = false;
        Camera.Heading = 0;
        Camera.Pitch = -40;
        Camera.Roll = 0;
        float size = MathF.Max(box.Size.X, box.Size.Y);
        float distance = size * 0.5f / MathF.Tan(Camera.FieldOfView * MapMath.DegreesToRadians / 2) * 1.1f;
        Camera.Position = box.Center - Camera.Forward * distance;
    }

    /// <summary>Puts the camera so that it looks at a point from its current direction.</summary>
    public void LookAt(Vector3 point, float distance)
    {
        if (Camera.Orthographic)
        {
            Camera.Position = point - Camera.Forward * 20000;
            return;
        }

        Camera.Position = point - Camera.Forward * distance;
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360;
        return degrees > 180 ? degrees - 360 : degrees <= -180 ? degrees + 360 : degrees;
    }
}
