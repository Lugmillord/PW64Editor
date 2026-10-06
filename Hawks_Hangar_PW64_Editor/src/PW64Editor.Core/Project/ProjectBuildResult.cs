using PW64Editor.Core.Build;

namespace PW64Editor.Core.Project;

/// <summary>
/// The outcome of <see cref="ProjectBuilder.Build"/>.
/// </summary>
/// <param name="RomBuild">The built ROM and layout details.</param>
/// <param name="AppliedOverrides">How many replacement files were used.</param>
public sealed record ProjectBuildResult(RomBuildResult RomBuild, int AppliedOverrides);
