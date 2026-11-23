using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The preview's ‹ and › over the picture's row (G19): laid over the picture, never its child; faint while the pointer is
/// away from the row and at full strength while it is over the picture or either button, as Avalonia's pointer-over pass
/// raises its entered and exited events on the row.
/// </summary>
[TestClass]
public sealed class PreviewStepButtonsTests
{
    [TestMethod]
    public void TheButtonsAreFaintWhileThePointerIsAwayFromThePicturesRowAndAtFullStrengthWhileItIsOverIt()
    {
        ControlThread.Run(() =>
        {
            var buttons = new PreviewStepButtons();
            var picture = new Border();
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);

            var row = buttons.Over(picture);

            CollectionAssert.AreEqual(new Control[] { picture, buttons }, row.Children.ToList(), "over the picture, never its child");
            Assert.IsLessThanOrEqualTo(0.5, buttons.Opacity, "faint with the pointer away");

            // Onto the picture: the pass enters the picture, then the row.
            Raise(InputElement.PointerEnteredEvent, pointer, picture, row);
            Assert.IsTrue(row.IsPointerOver);
            Assert.AreEqual(1d, buttons.Opacity, "full strength over the picture");

            // From the picture onto a button: the row stays entered, so nothing is raised on it.
            Raise(InputElement.PointerExitedEvent, pointer, picture);
            Raise(InputElement.PointerEnteredEvent, pointer, buttons.On, buttons);
            Assert.AreEqual(1d, buttons.Opacity, "full strength over a button");

            // Off the row: the pass exits the button, the panel, then the row.
            Raise(InputElement.PointerExitedEvent, pointer, buttons.On, buttons, row);
            Assert.IsFalse(row.IsPointerOver);
            Assert.IsLessThanOrEqualTo(0.5, buttons.Opacity, "faint again once the pointer leaves");
        });
    }

    /// <summary>Raises <paramref name="routedEvent"/> on each of <paramref name="branch"/> in turn, leaf first, as the pointer-over pass does.</summary>
    private static void Raise(RoutedEvent<PointerEventArgs> routedEvent, IPointer pointer, params InputElement[] branch)
    {
        foreach (var element in branch)
        {
            element.RaiseEvent(new PointerEventArgs(routedEvent, element, pointer, null, default, 0, PointerPointProperties.None, KeyModifiers.None));
        }
    }
}
