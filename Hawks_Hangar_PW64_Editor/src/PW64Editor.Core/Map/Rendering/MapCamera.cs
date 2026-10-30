using System.Numerics;

namespace PW64Editor.Core.Map.Rendering;

/// <summary>The fixed directions of the axis views.</summary>
public enum AxisView
{
    /// <summary>From above, north up.</summary>
    Top,

    /// <summary>From the south, looking north.</summary>
    South,

    /// <summary>From the north, looking south.</summary>
    North,

    /// <summary>From the west, looking east.</summary>
    West,

    /// <summary>From the east, looking west.</summary>
    East,
}

/// <summary>
/// The editor's camera: a position, a direction (heading, pitch, roll in degrees) and a field of view; or a parallel
/// projection for the axis views.
/// </summary>
/// <remarks>Heading 0 looks north (+y), 90 west (the game's sense: turning left about z); pitch −90 looks straight down.</remarks>
public sealed class MapCamera
{
    /// <summary>The nearest distance drawn.</summary>
    public const float Near = 2f;

    /// <summary>The farthest distance drawn.</summary>
    public const float Far = 60000f;

    /// <summary>Where the camera is (game units).</summary>
    public Vector3 Position { get; set; } = new(0, -1500, 900);

    /// <summary>Heading in degrees (0 = north, turning left).</summary>
    public float Heading { get; set; }

    /// <summary>Pitch in degrees (−90 = straight down, 90 = straight up).</summary>
    public float Pitch { get; set; } = -30;

    /// <summary>Roll in degrees.</summary>
    public float Roll { get; set; }

    /// <summary>The vertical field of view in degrees (perspective).</summary>
    public float FieldOfView { get; set; } = 50;

    /// <summary>True for a parallel projection (axis views).</summary>
    public bool Orthographic { get; set; }

    /// <summary>The height of the visible area in game units (parallel projection).</summary>
    public float OrthographicHeight { get; set; } = 2000;

    /// <summary>The direction the camera looks.</summary>
    public Vector3 Forward
    {
        get
        {
            float h = Heading * MapMath.DegreesToRadians, p = Math.Clamp(Pitch, -89.9f, 89.9f) * MapMath.DegreesToRadians;
            return new Vector3(-MathF.Sin(h) * MathF.Cos(p), MathF.Cos(h) * MathF.Cos(p), MathF.Sin(p));
        }
    }

    /// <summary>The direction to the right of the view.</summary>
    public Vector3 Right
    {
        get
        {
            float h = Heading * MapMath.DegreesToRadians, r = Roll * MapMath.DegreesToRadians;
            Vector3 flat = new(MathF.Cos(h), MathF.Sin(h), 0);
            return r == 0 ? flat : Vector3.Transform(flat, Matrix4x4.CreateFromAxisAngle(Forward, r));
        }
    }

    /// <summary>The direction up in the view.</summary>
    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Forward));

    /// <summary>The view matrix (world → camera).</summary>
    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

    /// <summary>The projection matrix for an image of the given aspect (width / height).</summary>
    public Matrix4x4 Projection(float aspect) => Orthographic
        ? Matrix4x4.CreateOrthographic(OrthographicHeight * aspect, OrthographicHeight, -Far, Far)
        : Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView * MapMath.DegreesToRadians, aspect, Near, Far);

    /// <summary>The game units one pixel covers at the given distance from the camera.</summary>
    public float UnitsPerPixel(float distance, int imageHeight) => Orthographic
        ? OrthographicHeight / Math.Max(1, imageHeight)
        : 2 * MathF.Max(distance, Near) * MathF.Tan(FieldOfView * MapMath.DegreesToRadians / 2) / Math.Max(1, imageHeight);

    /// <summary>The ray through a pixel: its start and its direction.</summary>
    public (Vector3 Origin, Vector3 Direction) Ray(float x, float y, int width, int height)
    {
        float nx = 2 * x / width - 1, ny = 1 - 2 * y / height;
        if (Orthographic)
        {
            Vector3 origin = Position + Right * (nx * OrthographicHeight * width / height / 2) + Up * (ny * OrthographicHeight / 2);
            return (origin, Forward);
        }

        float t = MathF.Tan(FieldOfView * MapMath.DegreesToRadians / 2);
        Vector3 direction = Forward + Right * (nx * t * width / height) + Up * (ny * t);
        return (Position, Vector3.Normalize(direction));
    }

    /// <summary>Sets one of the axis views, looking at <paramref name="target"/> with the given visible height.</summary>
    public void SetAxisView(AxisView view, Vector3 target, float visibleHeight)
    {
        Orthographic = true;
        OrthographicHeight = visibleHeight;
        Roll = 0;
        (Heading, Pitch) = view switch
        {
            AxisView.Top => (0f, -90f),
            AxisView.South => (0f, 0f),
            AxisView.North => (180f, 0f),
            AxisView.West => (-90f, 0f),
            _ => (90f, 0f),
        };
        Position = target - Forward * 20000;
    }

    /// <summary>A copy of the camera.</summary>
    public MapCamera Clone() => (MapCamera)MemberwiseClone();
}
