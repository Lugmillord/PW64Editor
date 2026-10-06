namespace PW64Editor.Core.Project;

/// <summary>
/// Thrown when a hack project cannot be created, loaded or built,
/// e.g. because a file is missing or the wrong base ROM is used.
/// </summary>
public sealed class ProjectException : Exception
{
    public ProjectException(string message)
        : base(message)
    {
    }

    public ProjectException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
