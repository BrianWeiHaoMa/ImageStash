using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using ImageStash.Look;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Look;

/// <summary>
/// The look's one home (F10): Paper is the one take and fills every key of <see cref="LookKeys.All"/> with a value of its
/// kind under light and under dark; the accent's marks follow the system's accent and the neutrals do not; a focused
/// field keeps its grey edges with its bottom edge in the accent, and the selected tile's ring stands off the tile inside
/// the space between tiles; a take applied replaces the one before in the application's resources, and the
/// application's accent handler rebuilds it; and no colour, brush, shadow or radius is set outside the home in the
/// windows' code, except the sites named with their reason.
/// </summary>
[TestClass]
public sealed class LooksTests
{
    private static readonly Color AccentA = Color.FromRgb(0x00, 0x67, 0xC0);
    private static readonly Color AccentB = Color.FromRgb(0xC2, 0x39, 0xB3);

    /// <summary>Keys every take builds from the accent (F10): what is chosen, has focus or answers the pointer's hover on a chip.</summary>
    private static readonly string[] AccentKeys =
        [LookKeys.ScopeChipHoverWords, LookKeys.SelectionRing, LookKeys.AccentRing, LookKeys.NoteGround];

    private static readonly string[] NeutralKeys =
        [LookKeys.Surface, LookKeys.Card, LookKeys.ScopeChipGround, LookKeys.ScopeChipBorder, LookKeys.TagChipGround, LookKeys.TileGround, LookKeys.KeyCapGround];

    public static IEnumerable<object[]> Variants => [[ThemeVariant.Light], [ThemeVariant.Dark]];

    [TestMethod]
    [DynamicData(nameof(Variants))]
    public void PaperFillsEveryKeyWithAValueOfItsKindAndNoOther(ThemeVariant variant)
    {
        var problems = Problems(Looks.All[0].Resources(variant, AccentA));

        Assert.IsEmpty(problems, $"Paper under {variant}: {string.Join("; ", problems)}");
    }

    [TestMethod]
    public void ATakeMissingAKeyOrFillingOneWithTheWrongKindIsNamedByThatKey()
    {
        var resources = Looks.All[0].Resources(ThemeVariant.Light, AccentA);
        resources.Remove(LookKeys.ChipRadius);
        resources[LookKeys.CardRadius] = new SolidColorBrush(AccentA);
        resources["ImageStash.NoSuchKey"] = new SolidColorBrush(AccentA);

        CollectionAssert.AreEquivalent(
            new[] { $"{LookKeys.ChipRadius} is missing", $"{LookKeys.CardRadius} holds no Radius", "ImageStash.NoSuchKey is not a key of the look" },
            Problems(resources).ToList());
    }

    [TestMethod]
    public void PaperIsTheOneTake()
    {
        Assert.HasCount(1, Looks.All, "one look, light and dark by Theme (G32)");
        Assert.AreEqual("Paper", Looks.All[0].Name);
        Assert.IsInstanceOfType<Paper>(Looks.All[0]);
    }

    [TestMethod]
    [DynamicData(nameof(Variants))]
    public void AFocusedFieldKeepsItsGreyEdgeOnEverySideWithItsBottomEdgeInTheAccent(ThemeVariant variant)
    {
        var (a, b) = (Looks.All[0].Resources(variant, AccentA), Looks.All[0].Resources(variant, AccentB));
        var thickness = (Thickness)a[LookKeys.FluentFieldFocusedThickness]!;
        var border = (ILinearGradientBrush)a[LookKeys.FluentFieldFocusedBorder]!;
        var edge = ColorOf(a[LookKeys.FluentFieldBorder]);
        var mark = ColorOf(a[LookKeys.SelectionRing]);

        Assert.IsTrue(thickness is { Left: > 0, Top: > 0, Right: > 0 }, $"every side keeps an edge: {thickness}");
        Assert.IsGreaterThan(thickness.Left, thickness.Bottom, "the bottom edge, the accent's, is the thicker");
        CollectionAssert.AreEquivalent(new[] { mark, edge }, border.GradientStops.Select(stop => stop.Color).Distinct().ToList(), "the accent's mark and the resting grey, nothing else");
        Assert.AreEqual(edge, Covering(border, thickness.Bottom), "above the bottom edge, the sides in the resting grey");
        Assert.AreEqual(mark, Covering(border, 0), "the bottom edge in the accent's mark");
        Assert.AreEqual(mark, Covering(border, thickness.Bottom - 1), "the whole of the bottom edge in the accent's mark");
        Assert.AreNotEqual(Covering(border, 0), Covering((ILinearGradientBrush)b[LookKeys.FluentFieldFocusedBorder]!, 0), "the system's accent, not a blue of Paper's own (F10)");
    }

    [TestMethod]
    [DynamicData(nameof(Variants))]
    public void TheSelectedTilesRingStandsOffTheTileInsideTheSpaceBetweenTiles(ThemeVariant variant)
    {
        var gap = ((Thickness)Looks.All[0].Resources(variant, AccentA)[LookKeys.SelectionRingGap]!).Left;

        // 2, the ring's width: ring and gap fit in the space between two tiles, so the ring never lies over a neighbour (G6).
        Assert.IsTrue(gap > 0 && gap + 2 < JustifiedRows.Gap, $"a gap of {gap} beside a ring of 2 in a space of {JustifiedRows.Gap}");
    }

    [TestMethod]
    [DynamicData(nameof(Variants))]
    public void TheAccentsMarksFollowTheAccentAndPapersOwnColoursDoNot(ThemeVariant variant)
    {
        var a = Looks.All[0].Resources(variant, AccentA);
        var b = Looks.All[0].Resources(variant, AccentB);

        foreach (var key in AccentKeys)
        {
            Assert.AreNotEqual(ColorOf(a[key]), ColorOf(b[key]), $"{variant}: {key} is built from the accent (F10)");
        }

        foreach (var key in NeutralKeys)
        {
            Assert.AreEqual(ColorOf(a[key]), ColorOf(b[key]), $"{variant}: {key} is Paper's own, whatever the accent");
        }
    }

    [TestMethod]
    [DynamicData(nameof(Variants))]
    public void PapersHoverWashAndLitChipAreTheAccents(ThemeVariant variant)
    {
        var paper = Looks.All[0];
        var (a, b) = (paper.Resources(variant, AccentA), paper.Resources(variant, AccentB));

        foreach (var key in new[] { LookKeys.ScopeChipHoverGround, LookKeys.IconButtonHoverGround, LookKeys.LitChipGround })
        {
            Assert.AreNotEqual(ColorOf(a[key]), ColorOf(b[key]), $"{key}: the light blue asked for is the accent's wash, never a blue of the take's own (F10)");
        }
    }

    [TestMethod]
    [DynamicData(nameof(Variants))]
    public void TheAccentsWordsReadOnTheirChipsWhateverTheSystemsAccent(ThemeVariant variant)
    {
        // A pale, an amber, a black, Windows' blue and a purple accent: the words the accent gives must still read.
        Color[] accents = [Color.FromRgb(0xFF, 0xFF, 0xCC), Color.FromRgb(0xFF, 0xB9, 0x00), Color.FromRgb(0, 0, 0), AccentA, AccentB];
        foreach (var accent in accents)
        {
            var look = Looks.All[0].Resources(variant, accent);
            var surface = ColorOf(look[LookKeys.Surface]);
            foreach (var (words, ground) in new[] { (LookKeys.ScopeChipHoverWords, LookKeys.ScopeChipHoverGround), (LookKeys.LitChipWords, LookKeys.LitChipGround), (LookKeys.IconButtonHoverWords, LookKeys.IconButtonHoverGround) })
            {
                var under = Over(ColorOf(look[ground]), surface);
                var contrast = Take.Contrast(Over(ColorOf(look[words]), under), under);
                Assert.IsGreaterThanOrEqualTo(4.5, contrast, $"{variant}, accent {accent}: {words} on {ground} reads at {contrast:0.00} to 1");
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(Variants))]
    public void TheTileGroundIsANeutralGrey(ThemeVariant variant)
    {
        var tile = ColorOf(Looks.All[0].Resources(variant, AccentA)[LookKeys.TileGround]);

        Assert.IsTrue(tile.A == 255 && tile.R == tile.G && tile.G == tile.B, $"{variant}: a transparent picture is never tinted (G6): {tile}");
    }

    [TestMethod]
    public void ATakeAppliedReplacesTheOneBeforeUnderLightAndDarkAndAddsItsRulesOnce()
    {
        var app = new Application();
        var other = new OtherTake();

        Looks.Apply(app, Looks.All[0], AccentA);
        Looks.Apply(app, other, AccentA);

        Assert.AreSame(other, Looks.Current(app)?.Take);
        Assert.HasCount(1, app.Resources.MergedDictionaries, "one take in the resources at a time");
        Assert.AreEqual(1, app.Styles.OfType<LookStyles>().Count());
        Assert.AreEqual(OtherTake.Light, Resolved(app, LookKeys.Surface, ThemeVariant.Light));
        Assert.AreEqual(OtherTake.Dark, Resolved(app, LookKeys.Surface, ThemeVariant.Dark));
    }

    [TestMethod]
    public void TheApplicationsAccentHandlerRebuildsTheTakeShownFromTheNewAccent()
    {
        var app = new Application();
        var take = Looks.All[0];
        Looks.Apply(app, take, AccentA);
        var before = Resolved(app, LookKeys.LitChipGround, ThemeVariant.Light);

        App.FollowAccent(app, new PlatformColorValues { AccentColor1 = AccentB });

        Assert.AreSame(take, Looks.Current(app)?.Take, "the same take, rebuilt");
        Assert.AreEqual(AccentB, Looks.Current(app)?.Accent);
        Assert.AreEqual(ColorOf(take.Resources(ThemeVariant.Light, AccentB)[LookKeys.LitChipGround]), Resolved(app, LookKeys.LitChipGround, ThemeVariant.Light));
        Assert.AreNotEqual(before, Resolved(app, LookKeys.LitChipGround, ThemeVariant.Light));
        Assert.HasCount(1, app.Resources.MergedDictionaries);
    }

    [TestMethod]
    public void NoColourBrushShadowOrRadiusIsSetOutsideTheHomeButAtTheNamedSites()
    {
        var root = RepositoryRoot.Above(AppContext.BaseDirectory) ?? throw new AssertFailedException("no repository root above the tests");

        var found = LookHome.Strays(Path.Combine(root, "src", "ImageStash"));

        Assert.IsEmpty(found, string.Join(Environment.NewLine, found));
    }

    [TestMethod]
    public void TheScanFindsALiteralLeftOrAddedAtASite()
    {
        // The wrong builds the scan exists for: a card's radius left in place, a drawn radius and a radius constant
        // no CornerRadius pattern finds, a colour added to the chips tomorrow, a Fluent key read at a site.
        string[] strays =
        [
            "        CornerRadius = new CornerRadius(8);",
            "        context.DrawRectangle(PillBackground, null, new RoundedRect(pill, 4));",
            "            context.FillRectangle(fill, strip, 4);",
            "    private const double Corner = 4;",
            "        chip.Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));",
            "        var outline = new Rectangle { RadiusX = 6, RadiusY = 6 };",
            "        BoxShadow = BoxShadows.Parse(\"0 4 16 0 #33000000\");",
            "            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 4 }),",
            "        card.Bind(Border.BackgroundProperty, card.GetResourceObservable(\"SystemControlBackgroundAltHighBrush\"));",
            "        var ring = new Pen(dark ? Brushes.White : Brushes.Black, 2);",
        ];
        foreach (var (file, line) in new[] { "Window/NoticeCard.cs", "Editor/EditorCanvas.cs", "Editor/Filmstrip.cs", "Window/ThumbnailGrid.cs", "Window/ChipRow.cs", "Window/DropTarget.cs", "Window/CaptureCard.cs", "Editor/EditorPane.cs", "Window/TagPicker.cs", "Window/SettingsWindow.cs" }.Zip(strays))
        {
            Assert.IsTrue(LookHome.IsStray(file, line), $"{file}: {line}");
        }

        string[] bound =
        [
            "        context.DrawRectangle(PillBackground, null, new RoundedRect(pill, look.Radius(LookKeys.SmallRadius)));",
            "                var rounded = new RoundedRect(box, corner);",
            "        card.Bind(Border.CornerRadiusProperty, card.GetResourceObservable(LookKeys.CardRadius));",
            "        context.FillRectangle(surround, strip, (float)look.Radius(LookKeys.SmallRadius));",
            "        Session.Colors.Use(color);",
            "            Background = Brushes.Transparent,",
        ];
        foreach (var line in bound)
        {
            Assert.IsFalse(LookHome.IsStray("Window/ChipRow.cs", line), line);
        }
    }

    /// <summary>What is wrong with a take's dictionary against <see cref="LookKeys.All"/>, each named by its key.</summary>
    private static IEnumerable<string> Problems(ResourceDictionary resources)
    {
        foreach (var (key, kind) in LookKeys.All)
        {
            if (!resources.TryGetValue(key, out var value))
            {
                yield return $"{key} is missing";
            }
            else if (!OfKind(value, kind))
            {
                yield return $"{key} holds no {kind}";
            }
        }

        foreach (var key in resources.Keys.OfType<string>().Where(key => LookKeys.All.All(entry => entry.Key != key)))
        {
            yield return $"{key} is not a key of the look";
        }
    }

    private static bool OfKind(object? value, LookKind kind) => kind switch
    {
        LookKind.Brush => value is IBrush,
        LookKind.Radius => value is CornerRadius,
        LookKind.Thickness => value is Thickness,
        LookKind.Shadow => value is BoxShadows,
        _ => false,
    };

    /// <summary><paramref name="top"/> as it shows over the opaque <paramref name="under"/>.</summary>
    private static Color Over(Color top, Color under)
    {
        var part = top.A / 255.0;
        byte Channel(byte a, byte b) => (byte)Math.Round(a * part + b * (1 - part));
        return Color.FromRgb(Channel(top.R, under.R), Channel(top.G, under.G), Channel(top.B, under.B));
    }

    /// <summary>
    /// The colour <paramref name="border"/> paints the line of pixels starting <paramref name="fromBottom"/> units above a
    /// box's bottom, whatever the box's height: its stop at that distance along its span, its span turned upside down
    /// about the box's centre.
    /// </summary>
    private static Color Covering(ILinearGradientBrush border, double fromBottom)
    {
        Assert.AreEqual(RelativeUnit.Absolute, border.StartPoint.Unit, "a span in units, whatever the box's height");
        Assert.AreEqual(Matrix.CreateScale(1, -1), border.Transform?.Value, "turned upside down");
        Assert.AreEqual(RelativePoint.Center, border.TransformOrigin, "about the box's centre, so its top lands on the bottom edge");
        var along = (fromBottom + 0.5 - border.StartPoint.Point.Y) / (border.EndPoint.Point.Y - border.StartPoint.Point.Y);
        return border.GradientStops.Last(stop => stop.Offset <= Math.Clamp(along, 0, 1)).Color;
    }

    private static Color ColorOf(object? brush) => brush is ISolidColorBrush solid ? solid.Color : throw new AssertFailedException($"{brush} is no solid brush");

    private static Color Resolved(Application app, string key, ThemeVariant variant) =>
        app.TryGetResource(key, variant, out var value) ? ColorOf(value) : throw new AssertFailedException($"{key} is not in the application's resources under {variant}");

    /// <summary>A take of the check's own: Paper's values with a ground of its own, so a switch shows.</summary>
    private sealed class OtherTake : Take
    {
        public static readonly Color Light = Color.FromRgb(0xFA, 0xF0, 0xE6);
        public static readonly Color Dark = Color.FromRgb(0x10, 0x20, 0x30);

        public override string Name => "The check's own";

        protected override void Fill(ResourceDictionary into, bool dark, Color accent)
        {
            foreach (var (key, value) in Looks.All[0].Resources(dark ? ThemeVariant.Dark : ThemeVariant.Light, accent))
            {
                into[key] = value;
            }

            into[LookKeys.Surface] = new SolidColorBrush(dark ? Dark : Light);
        }
    }
}

/// <summary>
/// The one-home scan: in the windows' code (<c>Window/</c>, <c>Editor/</c>, <c>Capture/</c> and <c>App.cs</c>), a line
/// that sets a colour, brush, shadow or radius of its own, or reads a resource by a key written out rather than from
/// <see cref="LookKeys"/>, is a stray unless it is one of the named sites, each kept with its reason.
/// </summary>
internal static class LookHome
{
    private static readonly Regex[] Literals =
    [
        new(@"Color\.From"),
        new(@"(?<![.\w])Colors\."),
        new(@"(?<![.\w])Brushes\.(?!Transparent\b)"),
        new(@"new SolidColorBrush\("),
        new(@"new CornerRadius\("),
        new(@"Radius[XY]\s*=\s*[\d.]+"),
        new(@"new RoundedRect\(.*,\s*[\d.]+\s*\)"),
        new(@"FillRectangle\(.*,\s*[\d.]+f?\s*\)"),
        new(@"(const|static readonly)\s+double\s+Corner\b"),
        new(@"BoxShadows\.Parse\("),
        new(@"new BoxShadow\b"),
        new(@"GetResourceObservable\("""),
        new(@"TryFindResource\("""),
    ];

    /// <summary>The sites that keep a colour, brush or radius of their own, by file and a span of the line, with the reason.</summary>
    private static readonly (string File, string Span, string Reason)[] Kept =
    [
        ("Capture/RecordingFrame.cs", "Red", "the recording's red frame and REC label, the same under every take (G15)"),
        ("Capture/RecordingFrame.cs", "Brushes.White", "the REC label's white on the recording's red (G15)"),
        ("Capture/RecordingBar.cs", "RecordingFrame.Red", "Record and Stop in the recording's red (G15)"),
        ("Capture/RecordingBar.cs", "RedHovered", "Record and Stop hovered and pressed, shades of the recording's red (G15)"),
        ("Capture/RecordingBar.cs", "RedPressed", "Record and Stop pressed, a shade of the recording's red (G15)"),
        ("Capture/RecordingBar.cs", "Brushes.White", "the white mark and words on the recording's red (G15)"),
        ("Capture/RegionOverlay.cs", "Dim =", "the capture overlay's dim, drawn over the frozen screen"),
        ("Capture/RegionOverlay.cs", "PillBackground =", "the capture overlay's pills, drawn over the frozen screen"),
        ("Capture/RegionOverlay.cs", "Outline =", "the capture overlay's outline, drawn over the frozen screen"),
        ("Capture/RegionOverlay.cs", "Brushes.White", "the capture overlay's words, white on its dark pills"),
        ("Capture/RegionCursor.cs", "Light =", "the capture overlay's cross, light over any screen and the same under every take"),
        ("Capture/RegionCursor.cs", "Dark =", "the capture overlay's cross's dark edge, over any screen and the same under every take"),
        ("Editor/EditorCanvas.cs", "Dimmed =", "the editor canvas's dim outside the crop"),
        ("Editor/EditorCanvas.cs", "FramePen =", "the crop frame's white line"),
        ("Editor/EditorCanvas.cs", "PillBackground =", "the crop size's dark pill over the picture"),
        ("Editor/EditorCanvas.cs", "Brushes.White", "the handles' white and the pill's words over the picture"),
        ("Editor/Filmstrip.cs", "PlayheadColor", "the filmstrip's red playhead"),
        ("Editor/Filmstrip.cs", "Dim =", "the filmstrip's dim over the frames left out"),
        ("Editor/Filmstrip.cs", "PillBackground =", "the filmstrip's busy pill over the frames"),
        ("Editor/Filmstrip.cs", "Brushes.White", "the handles' edge and the pill's words over the frames"),
        ("Editor/ColorPicker.cs", "new CornerRadius(11)", "the outline choice's round, a circle, not a rounded rectangle"),
        ("Editor/ColorPicker.cs", "ToColor(pure)", "the picking area's own hue"),
        ("Editor/ColorPicker.cs", "Gradient(", "the picking area's own white and black"),
        ("Editor/ColorPicker.cs", "Brushes.White", "the picking marks' white over the colours"),
        ("Editor/ColorPicker.cs", "area.Height / 2", "the hue strip, a capsule whose radius is half its height"),
        ("Editor/EditorButtons.cs", "GradientStop(Colors.", "the More swatch's rainbow"),
        ("Editor/EditorButtons.cs", "ToColor(", "a swatch's own colour"),
        ("Editor/OutlineSwatch.cs", "ToColor(", "the outline swatch's own colours"),
        ("Editor/OutlineSwatch.cs", "StrikeBrush", "None's red strike, a mark of meaning"),
        ("Window/ThumbnailGrid.cs", "BadgeBack", "the tile badge's ground over the picture (G6)"),
        ("Window/ThumbnailGrid.cs", "Brushes.White", "the tile badge's words over the picture (G6)"),
        ("Window/PreviewPicture.cs", "Brushes.White", "the play mark's white on its pill over the picture"),
    ];

    private static readonly string[] Folders = ["Window", "Editor", "Capture"];

    /// <summary>Every stray line under <paramref name="source"/>, as "file:line: text".</summary>
    public static IReadOnlyList<string> Strays(string source)
    {
        var files = Folders.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(source, folder), "*.cs", SearchOption.AllDirectories))
            .Append(Path.Combine(source, "App.cs"));
        var strays = new List<string>();
        foreach (var path in files)
        {
            var file = Path.GetRelativePath(source, path).Replace('\\', '/');
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (IsStray(file, lines[i]))
                {
                    strays.Add($"{file}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        return strays;
    }

    /// <summary>Whether <paramref name="line"/> of <paramref name="file"/> sets a look of its own that no named site keeps.</summary>
    public static bool IsStray(string file, string line) =>
        !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
        && Literals.Any(literal => literal.IsMatch(line))
        && !Kept.Any(kept => kept.File == file && line.Contains(kept.Span, StringComparison.Ordinal));
}
