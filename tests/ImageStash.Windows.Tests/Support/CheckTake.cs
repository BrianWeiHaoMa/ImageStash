using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ImageStash.Look;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A take of the look of the checks' own: Paper with the grounds a check reads set to colours Paper does not use, light
/// and dark apart, so a switch to it shows on every window that follows a take, and a window left on the old one shows
/// too.
/// </summary>
public sealed class CheckTake : Take
{
    public override string Name => "The checks' own";

    /// <summary>The colour this take gives <paramref name="key"/> under <paramref name="variant"/>.</summary>
    public static Color ColorOf(string key, ThemeVariant variant) => ((ISolidColorBrush)new CheckTake().Resources(variant, Looks.DefaultAccent)[key]!).Color;

    protected override void Fill(ResourceDictionary into, bool dark, Color accent)
    {
        foreach (var (key, value) in Looks.All[0].Resources(dark ? ThemeVariant.Dark : ThemeVariant.Light, accent))
        {
            into[key] = value;
        }

        into[LookKeys.FluentWindowGround] = Solid(dark ? 0x10203Au : 0xF0E6D2u);
        into[LookKeys.Surface] = Solid(dark ? 0x12243Cu : 0xF2E8D4u);
        into[LookKeys.PageGround] = Solid(dark ? 0x14263Eu : 0xF4EAD6u);
        into[LookKeys.ScopeChipGround] = Solid(dark ? 0x162840u : 0xF6ECD8u);
        into[LookKeys.TagChipGround] = Solid(dark ? 0x182A42u : 0xF8EEDAu);
        into[LookKeys.LitChipGround] = Solid(dark ? 0x5A2E1Eu : 0xE07A50u);
        into[LookKeys.LitChipWords] = Solid(dark ? 0xFFF4E8u : 0x1E0A04u);
        into[LookKeys.KeyCapGround] = Solid(dark ? 0x1A2C44u : 0xFAF0DCu);
        into[LookKeys.TileGround] = Solid(dark ? 0x404040u : 0xC8C8C8u);
        into[LookKeys.PictureSurround] = Solid(dark ? 0x1C2E46u : 0xDCE6F0u);
    }
}
