using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>
/// A Shortcuts seam for the checks of the parts that drive it: it keeps what is registered and counts every
/// registration asked for, refuses the next one with a given reason when told, and raises a press or a release of its
/// own as the platform's thread would, on the check's thread.
/// </summary>
public sealed class RecordingShortcuts : IShortcuts
{
    private readonly Dictionary<Shortcut, Action> registered = [];
    private readonly Dictionary<Shortcut, string> refused = [];
    private string? refuseNext;
    private Action<Shortcut, string>? released;

    public IReadOnlyCollection<Shortcut> Registered => registered.Keys;

    /// <summary>Every shortcut a registration was asked for, in order, refused ones included.</summary>
    public List<Shortcut> Asked { get; } = [];

    /// <summary>How many times a registration was asked for, refused ones included.</summary>
    public int Registrations => Asked.Count;

    /// <summary>The next registration fails with this reason.</summary>
    public void RefuseNext(string reason) => refuseNext = reason;

    /// <summary>Every registration of <paramref name="shortcut"/> fails with this reason, as while another program holds it.</summary>
    public void Refuse(Shortcut shortcut, string reason) => refused[shortcut] = reason;

    /// <summary>Registrations of <paramref name="shortcut"/> succeed again, as once the program holding it has let go.</summary>
    public void Free(Shortcut shortcut) => refused.Remove(shortcut);

    public Outcome Register(Shortcut shortcut, Action pressed)
    {
        Asked.Add(shortcut);
        if (refuseNext is { } reason)
        {
            refuseNext = null;
            return new Failure("Register the shortcut", shortcut.Text, reason);
        }

        if (refused.TryGetValue(shortcut, out var held))
        {
            return new Failure("Register the shortcut", shortcut.Text, held);
        }

        registered[shortcut] = pressed;
        AfterRegistering?.Invoke(shortcut);
        return Outcome.Done;
    }

    /// <summary>Runs after each registration succeeds and before it returns, as the seam's own thread may act at once.</summary>
    public Action<Shortcut>? AfterRegistering { get; set; }

    /// <summary>Runs inside each release asked for, before it is done, as the seam's own thread may act meanwhile.</summary>
    public Action<Shortcut>? DuringRelease { get; set; }

    public void Release(Shortcut shortcut)
    {
        DuringRelease?.Invoke(shortcut);
        registered.Remove(shortcut);
    }

    public void ListenForReleases(Action<Shortcut, string> released) => this.released = released;

    /// <summary>A press as a person makes it; a shortcut not registered is pressed in vain, as the platform would deliver nothing.</summary>
    public bool Press(Shortcut shortcut)
    {
        if (!registered.TryGetValue(shortcut, out var pressed))
        {
            return false;
        }

        pressed();
        return true;
    }

    /// <summary>The seam lets go of <paramref name="shortcut"/> on its own, as a layout that makes it type a character does.</summary>
    public void Release(Shortcut shortcut, string reason)
    {
        registered.Remove(shortcut);
        released!(shortcut, reason);
    }

    /// <summary>The press callback the seam holds for <paramref name="shortcut"/>, run even after the seam let it go, as a press already in flight would be.</summary>
    public Action PressedCallback(Shortcut shortcut) => registered[shortcut];
}
