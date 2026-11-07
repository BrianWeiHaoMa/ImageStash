namespace ImageStash.Look;

/// <summary>What a key of the look holds, so a take can be checked to fill each with a value of its kind.</summary>
public enum LookKind
{
    /// <summary>An <c>IBrush</c>.</summary>
    Brush,

    /// <summary>A <c>CornerRadius</c>; a drawn rounded rectangle reads its top-left radius.</summary>
    Radius,

    /// <summary>A <c>Thickness</c>.</summary>
    Thickness,

    /// <summary>A card's <c>BoxShadows</c>; its geometry and colour are the take's.</summary>
    Shadow,
}

/// <summary>
/// Every resource key a take of the look fills, under light and under dark (G32, F10, the one home for the look): the
/// keys ImageStash's own controls bind to, under the prefix <c>ImageStash.</c>, then the Fluent keys a take restyles,
/// under Fluent's names. A control binds a key here and never sets a colour, brush, shadow or radius of its own, so a
/// change within a take lands in the take's one file. A new key is added to <see cref="All"/> and filled in every take.
/// </summary>
public static class LookKeys
{
    private const string Prefix = "ImageStash.";

    // The grounds and their words.

    /// <summary>The preview's, the editor's and a notification's ground, the stash window's own beneath them.</summary>
    public const string Surface = Prefix + "Surface";

    /// <summary>The Settings page's ground, round its cards.</summary>
    public const string PageGround = Prefix + "PageGround";

    /// <summary>The words read on every ground of the look, and the drawn marks that take their colour.</summary>
    public const string Words = Prefix + "Words";

    /// <summary>Words that say less: a prompt, a hint.</summary>
    public const string FaintWords = Prefix + "FaintWords";

    /// <summary>A hairline between parts: the footer's top edge, the chip row's separator.</summary>
    public const string Divider = Prefix + "Divider";

    // Cards: the notices, the notifications' cards, the lists, the tag field's suggestions, the drop card, the editor's
    // popovers and the Settings page's groups.

    public const string Card = Prefix + "Card";
    public const string CardBorder = Prefix + "CardBorder";
    public const string CardShadow = Prefix + "CardShadow";

    // The chips: the scopes (Recent, Favorites, All), the tags, and either while lit.

    public const string ScopeChipGround = Prefix + "ScopeChipGround";
    public const string ScopeChipBorder = Prefix + "ScopeChipBorder";
    public const string ScopeChipWords = Prefix + "ScopeChipWords";
    public const string ScopeChipHoverGround = Prefix + "ScopeChipHoverGround";
    public const string ScopeChipHoverBorder = Prefix + "ScopeChipHoverBorder";
    public const string ScopeChipHoverWords = Prefix + "ScopeChipHoverWords";
    public const string TagChipGround = Prefix + "TagChipGround";
    public const string TagChipBorder = Prefix + "TagChipBorder";
    public const string TagChipWords = Prefix + "TagChipWords";
    public const string TagChipHoverGround = Prefix + "TagChipHoverGround";
    public const string LitChipGround = Prefix + "LitChipGround";
    public const string LitChipBorder = Prefix + "LitChipBorder";
    public const string LitChipWords = Prefix + "LitChipWords";
    public const string LitChipHoverGround = Prefix + "LitChipHoverGround";

    // The head's icon buttons (the gear, + and the pin) and the editor's.

    public const string IconButtonGround = Prefix + "IconButtonGround";
    public const string IconButtonWords = Prefix + "IconButtonWords";
    public const string IconButtonHoverGround = Prefix + "IconButtonHoverGround";
    public const string IconButtonHoverWords = Prefix + "IconButtonHoverWords";
    public const string IconButtonPressedGround = Prefix + "IconButtonPressedGround";

    /// <summary>The pin's ground while the window is pinned.</summary>
    public const string IconButtonOnGround = Prefix + "IconButtonOnGround";

    // A shortcut field on the Settings page and its key caps.

    public const string ShortcutFieldGround = Prefix + "ShortcutFieldGround";
    public const string ShortcutFieldBorder = Prefix + "ShortcutFieldBorder";
    public const string KeyCapGround = Prefix + "KeyCapGround";
    public const string KeyCapBorder = Prefix + "KeyCapBorder";
    public const string KeyCapWords = Prefix + "KeyCapWords";

    // The accent's marks, each built from the system's accent (F10).

    /// <summary>A ring that says "here": a drag over a chip, a shortcut field recording, the drop card's outline.</summary>
    public const string AccentRing = Prefix + "AccentRing";

    /// <summary>The selection's mark: the selected tiles' ring, the editor's handles and outline, a layer's range, the text's caret.</summary>
    public const string SelectionRing = Prefix + "SelectionRing";

    /// <summary>The space between a selected tile's edge and its ring, the window's ground showing through it, the focus ring filling it while the grid has keyboard focus (G6, G7); its left value.</summary>
    public const string SelectionRingGap = Prefix + "SelectionRingGap";

    /// <summary>A note's wash: the editor's font fallback note.</summary>
    public const string NoteGround = Prefix + "NoteGround";

    // The grid and the pictures' surround.

    /// <summary>A tile's ground behind a picture that does not fill it or lets it through: a neutral grey (G6).</summary>
    public const string TileGround = Prefix + "TileGround";

    /// <summary>The mark on a tile whose picture could not be read.</summary>
    public const string TileMark = Prefix + "TileMark";

    /// <summary>The ring round the focused tile, in the gap inside its selection ring, and round the focused preview (G7).</summary>
    public const string FocusRing = Prefix + "FocusRing";

    /// <summary>The ground round a picture in the preview, the editor and the filmstrip.</summary>
    public const string PictureSurround = Prefix + "PictureSurround";

    /// <summary>A small ground drawn over a picture: the preview's play and pause.</summary>
    public const string PicturePill = Prefix + "PicturePill";

    // The Settings page's warning, the editor's swatches.

    public const string WarningGround = Prefix + "WarningGround";
    public const string WarningMark = Prefix + "WarningMark";

    /// <summary>The edge of a colour swatch, and of the outline swatch without an outline.</summary>
    public const string SwatchEdge = Prefix + "SwatchEdge";

    // The recording bar (G15): its ground, words, faint words and Cancel's ground while hovered. Record and Stop keep
    // the recording's red under every take.

    public const string BarGround = Prefix + "BarGround";
    public const string BarWords = Prefix + "BarWords";
    public const string BarFaintWords = Prefix + "BarFaintWords";
    public const string BarHoverGround = Prefix + "BarHoverGround";

    // The radii of every rounded rectangle inside the windows.

    /// <summary>The cards', the popovers' and the Settings page's groups.</summary>
    public const string CardRadius = Prefix + "CardRadius";

    /// <summary>The chip row's chips.</summary>
    public const string ChipRadius = Prefix + "ChipRadius";

    /// <summary>ImageStash's own small controls: a tag in the tag field, a key cap, a note, a thumbnail inside a card.</summary>
    public const string ControlRadius = Prefix + "ControlRadius";

    /// <summary>A tile in the grid; its focus ring follows it two pixels out.</summary>
    public const string TileRadius = Prefix + "TileRadius";

    /// <summary>The preview's and the editor's surround, and the filmstrip's.</summary>
    public const string SurroundRadius = Prefix + "SurroundRadius";

    /// <summary>The smallest: a badge, a swatch, a size label, the filmstrip's handles and range, the colour picker's area.</summary>
    public const string SmallRadius = Prefix + "SmallRadius";

    /// <summary>A capsule: the recording bar's buttons, the capture overlay's hint, the preview's play.</summary>
    public const string PillRadius = Prefix + "PillRadius";

    // Fluent's own keys a take restyles, which every Fluent control of the kind reads.

    /// <summary>A window's ground, the stash window's included.</summary>
    public const string FluentWindowGround = "SystemRegionBrush";

    /// <summary>Fluent's buttons', boxes' and lists' corners.</summary>
    public const string FluentControlRadius = "ControlCornerRadius";

    /// <summary>Fluent's menus', flyouts' and tooltips' corners.</summary>
    public const string FluentOverlayRadius = "OverlayCornerRadius";

    /// <summary>Fluent's plain buttons: Undo, Edit, Open folder, the editor's choices' neighbours.</summary>
    public const string FluentButtonGround = "ButtonBackground";
    public const string FluentButtonHoverGround = "ButtonBackgroundPointerOver";
    public const string FluentButtonPressedGround = "ButtonBackgroundPressed";
    public const string FluentButtonBorder = "ButtonBorderBrush";

    /// <summary>Fluent's toggle buttons while not chosen: the editor's tools, presets, swatches and thicknesses.</summary>
    public const string FluentChoiceGround = "ToggleButtonBackground";
    public const string FluentChoiceHoverGround = "ToggleButtonBackgroundPointerOver";
    public const string FluentChoicePressedGround = "ToggleButtonBackgroundPressed";

    public const string FluentFieldGround = "TextControlBackground";
    public const string FluentFieldHoverGround = "TextControlBackgroundPointerOver";
    public const string FluentFieldFocusedGround = "TextControlBackgroundFocused";
    public const string FluentFieldBorder = "TextControlBorderBrush";
    public const string FluentFieldHoverBorder = "TextControlBorderBrushPointerOver";

    /// <summary>A focused text box's border, all four sides in one brush: where the accent's mark shows on the search box and every other field.</summary>
    public const string FluentFieldFocusedBorder = "TextControlBorderBrushFocused";

    /// <summary>A focused text box's border thickness, each side its own.</summary>
    public const string FluentFieldFocusedThickness = "TextControlBorderThemeThicknessFocused";

    /// <summary>Every key, with what it holds; a take fills each under light and under dark.</summary>
    public static readonly IReadOnlyList<(string Key, LookKind Kind)> All =
    [
        (Surface, LookKind.Brush),
        (PageGround, LookKind.Brush),
        (Words, LookKind.Brush),
        (FaintWords, LookKind.Brush),
        (Divider, LookKind.Brush),
        (Card, LookKind.Brush),
        (CardBorder, LookKind.Brush),
        (CardShadow, LookKind.Shadow),
        (ScopeChipGround, LookKind.Brush),
        (ScopeChipBorder, LookKind.Brush),
        (ScopeChipWords, LookKind.Brush),
        (ScopeChipHoverGround, LookKind.Brush),
        (ScopeChipHoverBorder, LookKind.Brush),
        (ScopeChipHoverWords, LookKind.Brush),
        (TagChipGround, LookKind.Brush),
        (TagChipBorder, LookKind.Brush),
        (TagChipWords, LookKind.Brush),
        (TagChipHoverGround, LookKind.Brush),
        (LitChipGround, LookKind.Brush),
        (LitChipBorder, LookKind.Brush),
        (LitChipWords, LookKind.Brush),
        (LitChipHoverGround, LookKind.Brush),
        (IconButtonGround, LookKind.Brush),
        (IconButtonWords, LookKind.Brush),
        (IconButtonHoverGround, LookKind.Brush),
        (IconButtonHoverWords, LookKind.Brush),
        (IconButtonPressedGround, LookKind.Brush),
        (IconButtonOnGround, LookKind.Brush),
        (ShortcutFieldGround, LookKind.Brush),
        (ShortcutFieldBorder, LookKind.Brush),
        (KeyCapGround, LookKind.Brush),
        (KeyCapBorder, LookKind.Brush),
        (KeyCapWords, LookKind.Brush),
        (AccentRing, LookKind.Brush),
        (SelectionRing, LookKind.Brush),
        (SelectionRingGap, LookKind.Thickness),
        (NoteGround, LookKind.Brush),
        (TileGround, LookKind.Brush),
        (TileMark, LookKind.Brush),
        (FocusRing, LookKind.Brush),
        (PictureSurround, LookKind.Brush),
        (PicturePill, LookKind.Brush),
        (WarningGround, LookKind.Brush),
        (WarningMark, LookKind.Brush),
        (SwatchEdge, LookKind.Brush),
        (BarGround, LookKind.Brush),
        (BarWords, LookKind.Brush),
        (BarFaintWords, LookKind.Brush),
        (BarHoverGround, LookKind.Brush),
        (CardRadius, LookKind.Radius),
        (ChipRadius, LookKind.Radius),
        (ControlRadius, LookKind.Radius),
        (TileRadius, LookKind.Radius),
        (SurroundRadius, LookKind.Radius),
        (SmallRadius, LookKind.Radius),
        (PillRadius, LookKind.Radius),
        (FluentWindowGround, LookKind.Brush),
        (FluentControlRadius, LookKind.Radius),
        (FluentOverlayRadius, LookKind.Radius),
        (FluentButtonGround, LookKind.Brush),
        (FluentButtonHoverGround, LookKind.Brush),
        (FluentButtonPressedGround, LookKind.Brush),
        (FluentButtonBorder, LookKind.Brush),
        (FluentChoiceGround, LookKind.Brush),
        (FluentChoiceHoverGround, LookKind.Brush),
        (FluentChoicePressedGround, LookKind.Brush),
        (FluentFieldGround, LookKind.Brush),
        (FluentFieldHoverGround, LookKind.Brush),
        (FluentFieldFocusedGround, LookKind.Brush),
        (FluentFieldBorder, LookKind.Brush),
        (FluentFieldHoverBorder, LookKind.Brush),
        (FluentFieldFocusedBorder, LookKind.Brush),
        (FluentFieldFocusedThickness, LookKind.Thickness),
    ];
}
