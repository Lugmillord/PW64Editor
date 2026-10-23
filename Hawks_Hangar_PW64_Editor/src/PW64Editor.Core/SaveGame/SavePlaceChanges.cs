namespace PW64Editor.Core.SaveGame;

/// <summary>
/// How the missions of a project moved (a mission removed, the later tests of its class moved up): a saved game takes its
/// results and photos along to the new places (<see cref="SaveSlot.ConvertTo"/>).
/// </summary>
/// <param name="Moved">Old place → new place of each mission that moved.</param>
/// <param name="Removed">The places whose mission is gone (their results are dropped).</param>
public sealed record SavePlaceChanges(IReadOnlyDictionary<TestId, TestId> Moved, IReadOnlyCollection<TestId> Removed)
{
    /// <summary>No mission moved.</summary>
    public static SavePlaceChanges None { get; } = new(new Dictionary<TestId, TestId>(), []);

    /// <summary>True if nothing moved and nothing was removed.</summary>
    public bool IsEmpty => Moved.Count == 0 && Removed.Count == 0;

    /// <summary>The old place whose result belongs to a new place, or null if none (a new place, or the old one is gone).</summary>
    public TestId? Source(TestId place)
    {
        foreach ((TestId from, TestId to) in Moved)
        {
            if (to == place)
            {
                return from;
            }
        }

        return Moved.ContainsKey(place) || Removed.Contains(place) ? null : place;
    }

    /// <summary>The new place of an old one (the same if it did not move; also for a removed place).</summary>
    public TestId Target(TestId place) => Moved.TryGetValue(place, out TestId to) ? to : place;
}
