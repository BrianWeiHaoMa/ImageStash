using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ImageStash.Look;

/// <summary>
/// The look and its application (F10): <see cref="All"/> holds its one take, Paper, light and dark;
/// <see cref="Apply(Application, Take, Color)"/> puts a take into the application's resources in place of the one before,
/// under light and dark both, so every control bound to a key of <see cref="LookKeys"/> follows at once (a new system
/// accent, or the checks' own take) and Theme keeps choosing which of the two shows.
/// </summary>
public static class Looks
{
    /// <summary>The accent when the platform names none: Windows' own default blue (F10).</summary>
    public static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x67, 0xC0);

    /// <summary>The look's takes: Paper alone, the one the product applies.</summary>
    public static readonly IReadOnlyList<Take> All =
    [
        new Paper(),
    ];

    /// <summary>The system's accent as the platform reports it now, else <see cref="DefaultAccent"/>.</summary>
    public static Color Accent(Application app) => app.PlatformSettings?.GetColorValues().AccentColor1 is { A: > 0 } accent ? accent : DefaultAccent;

    /// <summary>The product's take applied with <paramref name="accent"/>, as <see cref="Apply(Application, Take, Color)"/>.</summary>
    public static void Apply(Application app, Color accent) => Apply(app, All[0], accent);

    /// <summary>
    /// <paramref name="take"/>, its accent's marks from <paramref name="accent"/>, in the application's resources in place
    /// of any take before it, with the rules that dress Fluent's controls in its keys added once.
    /// </summary>
    public static void Apply(Application app, Take take, Color accent)
    {
        if (!app.Styles.OfType<LookStyles>().Any())
        {
            app.Styles.Add(new LookStyles());
        }

        var applied = new Applied(take, accent);
        applied.ThemeDictionaries[ThemeVariant.Light] = take.Resources(ThemeVariant.Light, accent);
        applied.ThemeDictionaries[ThemeVariant.Dark] = take.Resources(ThemeVariant.Dark, accent);
        var merged = app.Resources.MergedDictionaries;
        var before = merged.OfType<Applied>().ToList();
        // The new one goes in before the old ones go, so no key is ever missing between them.
        merged.Add(applied);
        foreach (var old in before)
        {
            merged.Remove(old);
        }
    }

    /// <summary>The take in the application's resources now, with the accent it was built from; null before the first <see cref="Apply(Application, Take, Color)"/>.</summary>
    public static (Take Take, Color Accent)? Current(Application app) =>
        app.Resources.MergedDictionaries.OfType<Applied>().LastOrDefault() is { } applied ? (applied.Take, applied.Accent) : null;

    /// <summary>A take's resources as applied, so the next application finds and replaces them.</summary>
    private sealed class Applied(Take take, Color accent) : ResourceDictionary
    {
        public Take Take { get; } = take;

        public Color Accent { get; } = accent;
    }
}
