using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>A context menu for a tag chip or one row of Every tag. Its primary click remains the opener's.</summary>
internal sealed class TagManagementMenu : ContextMenu
{
    private readonly Button anchor;
    private readonly Listing listing;
    private readonly Organising organising;
    private readonly Func<TagCount?> countAtOrigin;
    private readonly PressOnce? heldKeys;
    private readonly ClipboardPaste? paste;
    private readonly Func<bool> originInView;
    private StashStore? issuer;
    private IInputElement? returnFocus;
    private bool restoreFocus;
    private TagRenameBox? renameBox;

    public TagManagementMenu(Button anchor, Listing listing, Organising organising, Func<TagCount?> countAtOrigin,
        PressOnce? heldKeys, ClipboardPaste? paste, Func<bool> originInView)
    {
        this.anchor = anchor;
        this.listing = listing;
        this.organising = organising;
        this.countAtOrigin = countAtOrigin;
        this.heldKeys = heldKeys;
        this.paste = paste;
        this.originInView = originInView;
        if (heldKeys is not null)
        {
            MenuKeys.TrackWindowKeys(this, heldKeys);
        }

        Opening += (_, e) => e.Cancel = !Offer();
        Closing += (_, _) =>
        {
            restoreFocus = InTagMenu(TopLevel.GetTopLevel(anchor)?.FocusManager.GetFocusedElement());
            ForgetOffer();
        };
        Closed += (_, _) => GiveFocusBack();
        anchor.DetachedFromVisualTree += (_, _) =>
        {
            InvalidateOffer();
            CancelRename();
        };
    }

    protected override Type StyleKeyOverride => typeof(ContextMenu);

    internal static bool InTagMenu(object? element) =>
        element is Visual visual && visual.GetSelfAndVisualAncestors().OfType<TagManagementMenu>().Any();

    private bool Offer()
    {
        if (IsOpen || renameBox is not null || !anchor.IsEffectivelyEnabled || listing.Store is not { } store ||
            countAtOrigin() is not { Items: > 0 } tag)
        {
            return false;
        }

        returnFocus = TopLevel.GetTopLevel(anchor)?.FocusManager.GetFocusedElement();
        issuer = store;
        var rename = new MenuItem { Header = "Rename tag…" };
        rename.Click += (_, _) =>
        {
            if (issuer == store && Items.Contains(rename) && countAtOrigin() is { Items: > 0 } current)
            {
                var before = returnFocus;
                ForgetOffer();
                Close();
                BeginRename(store, current.Tag, before);
            }
        };
        var remove = new MenuItem { Header = $"Remove from all {OrganisingNotice.Items(tag.Items)}" };
        remove.Click += (_, _) =>
        {
            if (issuer == store && Items.Contains(remove))
            {
                ForgetOffer();
                _ = organising.RemoveTagFromAll(store, tag);
            }
        };
        Items.Clear();
        Items.Add(rename);
        Items.Add(remove);
        listing.Changed += CheckOffer;
        return true;
    }

    private void CheckOffer()
    {
        if (listing.Store != issuer || countAtOrigin() is not { Items: > 0 })
        {
            InvalidateOffer();
        }
    }

    private void InvalidateOffer()
    {
        ForgetOffer();
        Close();
    }

    private void ForgetOffer()
    {
        issuer = null;
        listing.Changed -= CheckOffer;
    }

    private void GiveFocusBack()
    {
        var before = returnFocus;
        returnFocus = null;
        // A click elsewhere may already have given focus to another control.
        if (restoreFocus && before is Visual previous && TopLevel.GetTopLevel(previous) is { } root &&
            root.FocusManager.GetFocusedElement() is null)
        {
            before.Focus();
        }
    }

    internal void CancelRename() => renameBox?.Cancel();

    private void BeginRename(StashStore store, string tag, IInputElement? returnTo)
    {
        var content = anchor.Content;
        var width = anchor.Width;
        var padding = anchor.Padding;
        var box = new TagRenameBox(tag, heldKeys, paste, End);
        renameBox = box;
        if (anchor.Bounds.Width > 0)
        {
            anchor.Width = anchor.Bounds.Width;
        }

        anchor.Padding = default;
        anchor.Content = box;
        listing.Changed += CheckOrigin;
        anchor.LayoutUpdated += CheckLayout;
        if (TopLevel.GetTopLevel(anchor) is { } root)
        {
            root.UpdateLayout();
            if (!box.Focus())
            {
                box.Cancel();
            }
        }

        box.SelectAll();

        void CheckLayout(object? sender, EventArgs e) => CheckOrigin();

        void CheckOrigin()
        {
            if (!OriginStillHere())
            {
                CancelRename();
            }
        }

        bool OriginStillHere() => listing.Store == store && countAtOrigin() is { Items: > 0 } current
            && StringComparer.OrdinalIgnoreCase.Equals(tag, current.Tag)
            && (TopLevel.GetTopLevel(anchor) is null || originInView());

        void End(TagRenameBox source, string? text)
        {
            var restore = source.IsFocused;
            var submitted = text != tag && OriginStillHere() ? text : null;
            listing.Changed -= CheckOrigin;
            anchor.LayoutUpdated -= CheckLayout;
            renameBox = null;
            anchor.Content = content;
            anchor.Width = width;
            anchor.Padding = padding;
            if (restore && returnTo is Visual control && TopLevel.GetTopLevel(control) is not null)
            {
                returnTo.Focus();
            }

            if (submitted is not null)
            {
                _ = organising.RenameTag(store, tag, submitted);
            }
        }
    }
}
