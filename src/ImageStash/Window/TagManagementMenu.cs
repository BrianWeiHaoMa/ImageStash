using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>A context menu for a tag chip or one row of Every tag. Its primary click remains the opener's.</summary>
internal sealed class TagManagementMenu : ContextMenu
{
    private readonly Control anchor;
    private readonly Listing listing;
    private readonly Organising organising;
    private readonly Func<TagCount?> countAtOrigin;
    private MenuItem? offeredRemoval;
    private StashStore? issuer;
    private IInputElement? returnFocus;
    private bool restoreFocus;

    public TagManagementMenu(Control anchor, Listing listing, Organising organising, Func<TagCount?> countAtOrigin, PressOnce? heldKeys)
    {
        this.anchor = anchor;
        this.listing = listing;
        this.organising = organising;
        this.countAtOrigin = countAtOrigin;
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
        anchor.DetachedFromVisualTree += (_, _) => InvalidateOffer();
    }

    protected override Type StyleKeyOverride => typeof(ContextMenu);

    internal static bool InTagMenu(object? element) =>
        element is Visual visual && visual.GetSelfAndVisualAncestors().OfType<TagManagementMenu>().Any();

    private bool Offer()
    {
        if (IsOpen || !anchor.IsEffectivelyEnabled || listing.Store is not { } store ||
            countAtOrigin() is not { Items: > 0 } tag)
        {
            return false;
        }

        returnFocus = TopLevel.GetTopLevel(anchor)?.FocusManager.GetFocusedElement();
        issuer = store;
        var remove = new MenuItem { Header = $"Remove from all {OrganisingNotice.Items(tag.Items)}" };
        remove.Click += (_, _) =>
        {
            if (offeredRemoval == remove)
            {
                ForgetOffer();
                _ = organising.RemoveTagFromAll(store, tag);
            }
        };
        offeredRemoval = remove;
        Items.Clear();
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
        offeredRemoval = null;
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
}
