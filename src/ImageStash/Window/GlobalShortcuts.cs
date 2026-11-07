using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>The acts of F5's global shortcuts that are built; a shortcut is registered only once its act is (R10).</summary>
public enum ShortcutAct
{
    Open,
    Screenshot,
    Record,
    StashClipboardImage,
}

/// <summary>
/// A global shortcut's act, run at each press on the UI thread, or, when <paramref name="AtThePress"/>, on the Shortcuts
/// seam's thread, for an act that reads something at the press itself and returns quickly there.
/// </summary>
public sealed record ShortcutBinding(ShortcutAct Act, Action Pressed, bool AtThePress = false);

/// <summary>
/// G33's and G34's policy for every global shortcut whose act is built, the only caller of the Shortcuts seam: each,
/// read from the settings' text, is registered at start; one that cannot be registered, or that the seam releases
/// because it would type a character, is unavailable, named in the tray's tooltip, and stays unregistered, since it is
/// left so "until it is changed in Settings" and nothing retries it, while the others and everything else keep working;
/// Pause shortcuts releases what is registered and its toggle registers those same ones again, under the same checks.
/// Settings changes a shortcut by <see cref="Rebind"/> while a field records under <see cref="Suspend"/> (G32). Pause is
/// not kept in the settings file (State: Settings), so every start registers afresh.
/// </summary>
public sealed class GlobalShortcuts
{
    /// <summary>Why a Settings field refuses the plain <c>Ctrl+V</c>: registered, it would swallow G8's own paste keystroke.</summary>
    public const string PasteKeystrokeReason = "it is the paste keystroke the handoff sends";

    /// <summary>Why a Settings field records no combination whose modifiers came up before its key, when nothing else refuses it.</summary>
    public const string ModifiersLetGoFirstReason = "its modifiers came up before the key, so it was not recorded; hold them until the key is up";

    private static readonly KeyGesture PasteKeystroke = new(Key.V, KeyModifiers.Control);

    private readonly SettingsKeeper settings;
    private readonly IShortcuts seam;
    private readonly Action<Failure> name;
    private readonly Action<Action> onUiThread;
    private readonly IReadOnlyList<Bound> bound;
    // Read on the seam's thread at a press or a release, changed on the UI thread; both under this lock. The seam is
    // never called under it: a press waits on it on the seam's thread while a call from the UI thread waits for that thread.
    private readonly object gate = new();
    private readonly HashSet<Bound> registered = [];
    private bool paused;
    private bool suspended;

    /// <param name="settings">The settings in effect, whose shortcuts' texts are bound now and which a rebind writes.</param>
    /// <param name="bindings">The acts whose shortcuts are built, in the order they are registered at start.</param>
    /// <param name="name">Names a shortcut that is unavailable, and why, in the tray's tooltip; called on the UI thread.</param>
    /// <param name="onUiThread">Runs an action on the UI thread later, for a press and for what the seam reports on its own thread.</param>
    public GlobalShortcuts(SettingsKeeper settings, IReadOnlyList<ShortcutBinding> bindings, IShortcuts seam, Action<Failure> name, Action<Action> onUiThread)
    {
        this.settings = settings;
        this.seam = seam;
        this.name = name;
        this.onUiThread = onUiThread;
        var texts = settings.Current.Shortcuts;
        bound = bindings.Select(binding => new Bound(binding, TextIn(texts, binding.Act))).ToList();
    }

    /// <summary>
    /// F5's four shortcuts, as ImageStash binds them: the open shortcut, whose press reads the window in front at the
    /// press on the seam's thread (G1, G8), then Take a screenshot (G14), Record a clip, whose press also stops the
    /// recording (G15), and Stash clipboard image (G13), whose acts run on the UI thread.
    /// </summary>
    public static IReadOnlyList<ShortcutBinding> Bindings(Action summon, Action screenshot, Action record, Action stashClipboardImage) =>
    [
        new(ShortcutAct.Open, summon, AtThePress: true),
        new(ShortcutAct.Screenshot, screenshot),
        new(ShortcutAct.Record, record),
        new(ShortcutAct.StashClipboardImage, stashClipboardImage),
    ];

    /// <summary>The shortcut of <paramref name="act"/> as the settings read, registered or not; null when its text is not a shortcut.</summary>
    public Shortcut? ShortcutOf(ShortcutAct act)
    {
        lock (gate)
        {
            return Of(act).Shortcut;
        }
    }

    /// <summary>The setting of <paramref name="act"/>'s shortcut as written, which is its text when it is a shortcut.</summary>
    public string TextOf(ShortcutAct act)
    {
        lock (gate)
        {
            return Of(act).Text;
        }
    }

    /// <summary>
    /// Why <paramref name="act"/>'s shortcut is unregistered, as the tooltip names it (G33): refused at start, released by
    /// the seam for the layout, or refused at Pause's toggle back; null while it is registered or held off only by Pause or
    /// a recording, and from its next successful <see cref="Rebind"/>.
    /// </summary>
    public Failure? UnavailableReason(ShortcutAct act)
    {
        lock (gate)
        {
            return Of(act).Unavailable;
        }
    }

    /// <summary>The first act, in F5's order, whose shortcut is unavailable; Settings opens on its row (G33).</summary>
    public ShortcutAct? FirstUnavailable
    {
        get
        {
            lock (gate)
            {
                return bound.FirstOrDefault(each => each.Unavailable is not null)?.Binding.Act;
            }
        }
    }

    /// <summary>The action every failure to register <paramref name="act"/>'s shortcut names.</summary>
    public static string RegistrationAction(ShortcutAct act) => $"Register {ShortName(act)}";

    public bool Paused
    {
        get
        {
            lock (gate)
            {
                return paused;
            }
        }
    }

    /// <summary>
    /// Raised on the UI thread when <see cref="Paused"/> changes, when a shortcut is rebound, and when one becomes
    /// unavailable after start, so the tray and an open Settings page read the shortcuts again.
    /// </summary>
    public event Action? Changed;

    /// <summary>Registers every shortcut, the check "at start" (G33); one that fails is named and stops none of the others.</summary>
    public void Start()
    {
        seam.ListenForReleases(Released);
        RegisterEach(holdOff: false);
    }

    /// <summary>
    /// The shortcuts bound again from the settings in effect, which an import has replaced (G36): every registered one
    /// released, each act bound to its text there and registered under the checks of <see cref="Start"/>, one that fails
    /// unavailable and named and stopping none of the others (G33); while Pause or a recording holds them off, they are
    /// only due, and its end registers them. <see cref="Changed"/> is raised, so the tray, the window's <c>+</c> menu and
    /// an open Settings page show them (G34).
    /// </summary>
    public void BindFromSettings()
    {
        List<Shortcut> release;
        bool holdOff;
        lock (gate)
        {
            release = TakeRegistered();
            var texts = settings.Current.Shortcuts;
            foreach (var each in bound)
            {
                each.Bind(TextIn(texts, each.Binding.Act));
            }

            holdOff = paused || suspended;
        }

        foreach (var shortcut in release)
        {
            seam.Release(shortcut);
        }

        RegisterEach(holdOff);
        Changed?.Invoke();
    }

    /// <summary>
    /// Registers every shortcut, or with <paramref name="holdOff"/> only marks it due; one whose text is no shortcut, or
    /// that another of the four spells too, or that the seam refuses, is unavailable and named.
    /// </summary>
    private void RegisterEach(bool holdOff)
    {
        for (var i = 0; i < bound.Count; i++)
        {
            var each = bound[i];
            var reason = each.Parsed.Failure?.Reason
                // Two settings spelling one combination: the seam would take the second for another program's hold.
                ?? (bound.Take(i).FirstOrDefault(earlier => earlier.Shortcut == each.Shortcut) is { } earlier ? UsesItToo(earlier) : null)
                ?? (holdOff ? MarkDue(each) : Register(each).Failure?.Reason);
            if (reason is not null)
            {
                MakeUnavailable(each, reason);
            }
        }
    }

    /// <summary>Due without registering, while Pause or a recording holds the shortcuts off; nothing refuses it now.</summary>
    private string? MarkDue(Bound each)
    {
        lock (gate)
        {
            each.Due = true;
        }

        return null;
    }

    /// <summary>Pause shortcuts and its toggle back (G33, G34), from the tray menu on the UI thread.</summary>
    public void TogglePause()
    {
        List<Shortcut> release = [];
        List<Bound> again = [];
        lock (gate)
        {
            paused = !paused;
            if (paused)
            {
                release = TakeRegistered();
            }
            else if (!suspended)
            {
                again = Due();
            }
        }

        foreach (var shortcut in release)
        {
            seam.Release(shortcut);
        }

        RegisterAgain(again);
        Changed?.Invoke();
    }

    /// <summary>
    /// Releases every registered shortcut while a Settings field records a press, which a registered one would swallow
    /// before the field saw it (G32); disposing registers again every shortcut still due, unless Pause holds them off,
    /// whose toggle back then registers them. One recording at a time.
    /// </summary>
    public IDisposable Suspend()
    {
        List<Shortcut> release;
        lock (gate)
        {
            if (suspended)
            {
                throw new InvalidOperationException("A recording is already in progress.");
            }

            suspended = true;
            release = TakeRegistered();
        }

        foreach (var shortcut in release)
        {
            seam.Release(shortcut);
        }

        return new Suspension(this);
    }

    /// <summary>
    /// A Settings field's candidate for <paramref name="act"/>, while it records (G32, G33): the act's own shortcut while
    /// it is due changes nothing, under Pause once the seam has judged it (refused then, it is unavailable as at any registration);
    /// refused, in this order, the plain <c>Ctrl+V</c>, the combination of another of the four
    /// whether registered or not, and whatever the seam refuses (held by another program, typing a character), each named
    /// with the act, the candidate and why, nothing changed; otherwise the candidate is the act's shortcut from now on,
    /// registered in place of the old (released again at once under Pause, until its toggle back), written to the
    /// settings, the act no longer unavailable, and <see cref="Changed"/> raised.
    /// </summary>
    public Outcome Rebind(ShortcutAct act, Shortcut candidate)
    {
        var each = Of(act);
        bool own;
        lock (gate)
        {
            if (!suspended)
            {
                throw new InvalidOperationException("A shortcut is rebound only while a field records.");
            }

            own = OwnAndDue(each, candidate);
            if (own && !paused)
            {
                return Outcome.Done;
            }
        }

        if (Refusal(each, candidate) is { } refused)
        {
            return new Failure(RegistrationAction(act), candidate.Text, refused);
        }

        // The old one is already released by the suspension, so the seam judges the candidate alone (G33). It is bound,
        // and counted as registered, before the seam's call, as Register counts one, so a release the seam reports the
        // instant after registering it finds the act and names it; a refusal puts back what was bound before.
        Bound.Bindings before;
        bool heldOff;
        lock (gate)
        {
            before = each.Rebind(candidate);
            heldOff = paused;
            if (!heldOff)
            {
                registered.Add(each);
            }
        }

        var judged = seam.Register(candidate, () => Pressed(each, candidate));
        if (!judged.Succeeded)
        {
            lock (gate)
            {
                registered.Remove(each);
                each.Restore(before);
            }

            if (own)
            {
                OwnRefused(each, judged.Failure.Reason);
            }

            return new Failure(RegistrationAction(act), candidate.Text, judged.Failure.Reason);
        }

        if (heldOff)
        {
            seam.Release(candidate);
        }

        if (own)
        {
            return Outcome.Done;
        }

        settings.Change(s => s with { Shortcuts = WithText(s.Shortcuts, act, candidate.Text) });
        Changed?.Invoke();
        return Outcome.Done;
    }

    /// <summary>
    /// A Settings field's press whose key it saw only coming up (Windows withholds PrtScn's KeyDown; another program's hot
    /// key takes the press of the combination it holds), so which combination was pressed is only likely (G32):
    /// <paramref name="candidates"/>, the likeliest first. The first another program holds is named, whatever else would
    /// refuse the likeliest, since a holder is what took the press: the four are released while a field records and nobody
    /// takes the plain <c>Ctrl+V</c>, which is never asked of the seam; each the seam takes is let go at once. Otherwise the
    /// likeliest is a press, as <see cref="Rebind"/> takes one, unless <paramref name="letGoFirst"/>, one of its modifiers
    /// having come up before the key: then it is judged as <see cref="Rebind"/> judges one but never bound, since nobody
    /// may have pressed it, and when nothing refuses it it is refused with <see cref="ModifiersLetGoFirstReason"/>, unless
    /// it is the act's own, which changes nothing. The act's own shortcut, while due, refused by the seam here is unavailable.
    /// </summary>
    public Outcome JudgeSeenComingUp(ShortcutAct act, IReadOnlyList<Shortcut> candidates, bool letGoFirst)
    {
        var each = Of(act);
        var likeliest = candidates[0];
        lock (gate)
        {
            if (!suspended)
            {
                throw new InvalidOperationException("A shortcut is judged only while a field records.");
            }
        }

        Failure? likeliestRefused = null;
        foreach (var candidate in candidates.Distinct().Where(candidate => !candidate.Gesture.Equals(PasteKeystroke)))
        {
            // Asked of the seam only for its judgement: bound to no act, and let go at once when it is free.
            var judged = seam.Register(candidate, () => { });
            if (judged.Succeeded)
            {
                seam.Release(candidate);
            }
            else if (judged.Failure.Reason == ShortcutReasons.HeldByAnotherProgram)
            {
                return Refused(candidate, judged.Failure.Reason);
            }
            else if (candidate == likeliest)
            {
                likeliestRefused = judged.Failure;
            }
        }

        if (!letGoFirst)
        {
            return Rebind(act, likeliest);
        }

        bool own;
        lock (gate)
        {
            own = OwnAndDue(each, likeliest);
        }

        if (own && likeliestRefused is null)
        {
            return Outcome.Done;
        }

        if (Refusal(each, likeliest) is { } refused)
        {
            return new Failure(RegistrationAction(act), likeliest.Text, refused);
        }

        return likeliestRefused is null
            ? new Failure(RegistrationAction(act), likeliest.Text, ModifiersLetGoFirstReason)
            : Refused(likeliest, likeliestRefused.Reason);

        Failure Refused(Shortcut candidate, string reason)
        {
            bool ownRefused;
            lock (gate)
            {
                ownRefused = OwnAndDue(each, candidate);
            }

            if (ownRefused)
            {
                OwnRefused(each, reason);
            }

            return new Failure(RegistrationAction(act), candidate.Text, reason);
        }
    }

    /// <summary>
    /// On the UI thread: the act's own shortcut, still due, refused when a field judged it afresh (under Pause, or from a
    /// press the field saw only coming up), which registers it: unavailable until it is changed in Settings, and named, as
    /// at any other registration (G33).
    /// </summary>
    private void OwnRefused(Bound each, string reason)
    {
        lock (gate)
        {
            each.Due = false;
        }

        MakeUnavailable(each, reason);
        Changed?.Invoke();
    }

    /// <summary>
    /// Under the lock: whether a field's candidate is its act's own shortcut, still due. While Pause is off, that one was
    /// registered before the recording began and changes nothing; under Pause, another program may have taken it, or the
    /// layout come to type it, since, so it is judged afresh like any candidate (G32).
    /// </summary>
    private static bool OwnAndDue(Bound each, Shortcut candidate) => candidate == each.Shortcut && each.Due;

    /// <summary>What a field's candidate is refused for before the seam is asked: the plain <c>Ctrl+V</c>, then another of the four's combination, registered or not.</summary>
    private string? Refusal(Bound each, Shortcut candidate) =>
        candidate.Gesture.Equals(PasteKeystroke) ? PasteKeystrokeReason
        : bound.FirstOrDefault(other => other != each && other.Shortcut == candidate) is { } other ? UsesItToo(other)
        : null;

    private void Resume()
    {
        List<Bound> again = [];
        lock (gate)
        {
            suspended = false;
            if (!paused)
            {
                again = Due();
            }
        }

        if (RegisterAgain(again))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Under the lock: the shortcuts registered now, in the bindings' order, no longer counted as registered.</summary>
    private List<Shortcut> TakeRegistered()
    {
        var taken = bound.Where(registered.Contains).Select(each => each.Shortcut!).ToList();
        registered.Clear();
        return taken;
    }

    /// <summary>Under the lock: the shortcuts due to be registered and not registered now.</summary>
    private List<Bound> Due() => bound.Where(each => each.Due && !registered.Contains(each)).ToList();

    /// <summary>Registers each again under the same checks; one refused is unavailable and named. Whether any was.</summary>
    private bool RegisterAgain(List<Bound> again)
    {
        var anyRefused = false;
        foreach (var each in again)
        {
            if (Register(each).Failure is { } failure)
            {
                MakeUnavailable(each, failure.Reason);
                anyRefused = true;
            }
        }

        return anyRefused;
    }

    private Outcome Register(Bound each)
    {
        Shortcut shortcut;
        // Counted as registered before the seam's call, so a press at once after it is not taken for a released shortcut.
        lock (gate)
        {
            shortcut = each.Shortcut!;
            registered.Add(each);
            each.Due = true;
        }

        var outcome = seam.Register(shortcut, () => Pressed(each, shortcut));
        if (!outcome.Succeeded)
        {
            lock (gate)
            {
                registered.Remove(each);
                each.Due = false;
            }
        }

        return outcome;
    }

    /// <summary>A press, on the seam's thread, after the seam's own check before it acts (G33).</summary>
    private void Pressed(Bound each, Shortcut shortcut)
    {
        lock (gate)
        {
            // Pause and a recording empty the set, a shortcut the seam released leaves it, and a rebound one is no longer
            // the act's, so none of them acts.
            if (!registered.Contains(each) || each.Shortcut != shortcut)
            {
                return;
            }
        }

        if (each.Binding.AtThePress)
        {
            each.Binding.Pressed();
        }
        else
        {
            onUiThread(each.Binding.Pressed);
        }
    }

    /// <summary>
    /// The seam let go of a shortcut that would now type a character: it stays released, Pause and a recording
    /// included, even when the report arrives while either is letting it go, until it is changed in Settings; the other
    /// shortcuts are untouched.
    /// </summary>
    private void Released(Shortcut shortcut, string reason)
    {
        Bound? each;
        lock (gate)
        {
            each = bound.FirstOrDefault(r => r.Due && r.Shortcut == shortcut);
            if (each is null)
            {
                return;
            }

            registered.Remove(each);
            each.Due = false;
        }

        onUiThread(() =>
        {
            lock (gate)
            {
                // A rebind since then decided the act afresh, so this release no longer says which shortcut is unavailable.
                if (each.Due || each.Unavailable is not null || each.Shortcut != shortcut)
                {
                    return;
                }
            }

            MakeUnavailable(each, reason);
            Changed?.Invoke();
        });
    }

    /// <summary>On the UI thread: the act's shortcut is unavailable for this reason until it is changed in Settings, and is named.</summary>
    private void MakeUnavailable(Bound each, string reason)
    {
        // The tooltip's line says which shortcut, and names it as the settings spell it, an empty setting as such (G33,
        // G35); its action alone still says which, should the tooltip have to shorten the line to it.
        var failure = new Failure(RegistrationAction(each.Binding.Act), string.IsNullOrWhiteSpace(each.Text) ? "(empty)" : each.Text, reason);
        lock (gate)
        {
            each.Unavailable = failure;
        }

        name(failure);
    }

    private Bound Of(ShortcutAct act) => bound.Single(each => each.Binding.Act == act);

    private static string UsesItToo(Bound other) => $"{ShortName(other.Binding.Act)} uses it too";

    private static string ShortName(ShortcutAct act) => act switch
    {
        ShortcutAct.Open => "the open shortcut",
        ShortcutAct.Screenshot => "the screenshot shortcut",
        ShortcutAct.Record => "the record shortcut",
        ShortcutAct.StashClipboardImage => "the Stash clipboard image shortcut",
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    private static string TextIn(Shortcuts texts, ShortcutAct act) => act switch
    {
        ShortcutAct.Open => texts.Open,
        ShortcutAct.Screenshot => texts.Screenshot,
        ShortcutAct.Record => texts.Record,
        ShortcutAct.StashClipboardImage => texts.StashClipboardImage,
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    private static Shortcuts WithText(Shortcuts texts, ShortcutAct act, string text) => act switch
    {
        ShortcutAct.Open => texts with { Open = text },
        ShortcutAct.Screenshot => texts with { Screenshot = text },
        ShortcutAct.Record => texts with { Record = text },
        ShortcutAct.StashClipboardImage => texts with { StashClipboardImage = text },
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    /// <summary>A binding with its shortcut as the settings read it, or why the text is not one, and its state.</summary>
    private sealed class Bound(ShortcutBinding binding, string text)
    {
        public ShortcutBinding Binding { get; } = binding;

        // The rest changes on the UI thread, under the lock.
        public string Text { get; private set; } = text;

        public Outcome<Shortcut> Parsed { get; private set; } = Shortcut.Parse(text);

        public Shortcut? Shortcut => Parsed.Value;

        /// <summary>Registered, or released only while Pause or a recording holds it off, and registered again after.</summary>
        public bool Due { get; set; }

        public Failure? Unavailable { get; set; }

        /// <summary>Bound to <paramref name="candidate"/>, due and no longer unavailable; returns what was bound before.</summary>
        public Bindings Rebind(Shortcut candidate)
        {
            var before = new Bindings(Text, Parsed, Due, Unavailable);
            (Text, Parsed, Due, Unavailable) = (candidate.Text, Outcome<Shortcut>.Of(candidate), true, null);
            return before;
        }

        public void Restore(Bindings before) => (Text, Parsed, Due, Unavailable) = (before.Text, before.Parsed, before.Due, before.Unavailable);

        /// <summary>Bound afresh to <paramref name="text"/>, as at start: not yet due, and not unavailable until judged.</summary>
        public void Bind(string text) => (Text, Parsed, Due, Unavailable) = (text, Shortcut.Parse(text), false, null);

        /// <summary>What a binding held before a rebind, put back should the seam refuse the candidate.</summary>
        public readonly record struct Bindings(string Text, Outcome<Shortcut> Parsed, bool Due, Failure? Unavailable);
    }

    /// <summary>A recording's hold on the shortcuts, ended once.</summary>
    private sealed class Suspension(GlobalShortcuts shortcuts) : IDisposable
    {
        private bool ended;

        public void Dispose()
        {
            if (!ended)
            {
                ended = true;
                shortcuts.Resume();
            }
        }
    }
}
