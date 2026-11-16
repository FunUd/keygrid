using System.Windows.Input;
using KeyGrid.Core.Abstractions;
using KeyGrid.UI;

namespace KeyGrid.UI.Tests;

public class OverlayKeyHandlerTests
{
    [Fact]
    public void PrimaryOverlay_MapsKeyCommandsToSelectionEvents()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            var events = new List<string>();
            handler.KeyPressed += key => events.Add($"key:{key}");
            handler.MoveRequested += (column, row) => events.Add($"move:{column},{row}");
            handler.ConfirmRequested += clickKind => events.Add($"confirm:{clickKind}");
            handler.BackspacePressed += () => events.Add("backspace");
            handler.CancelRequested += () => events.Add("cancel");

            Assert.True(handler.HandleKeyDown(Key.Q, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Right, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Left, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Down, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Up, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Enter, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyUp(Key.Enter, ModifierKeys.None));
            Assert.True(handler.HandleKeyDown(Key.Enter, ModifierKeys.Shift, isRepeat: false));
            Assert.True(handler.HandleKeyUp(Key.Enter, ModifierKeys.Shift));
            foreach (var modifiers in new[]
            {
                ModifierKeys.Control,
                ModifierKeys.Alt,
                ModifierKeys.Windows,
                ModifierKeys.Shift | ModifierKeys.Control
            })
            {
                Assert.True(handler.HandleKeyDown(Key.Enter, modifiers, isRepeat: false));
                Assert.True(handler.HandleKeyUp(Key.Enter, modifiers));
            }
            Assert.True(handler.HandleKeyDown(Key.Back, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Escape, ModifierKeys.None, isRepeat: false));

            Assert.Equal(
                new[] { "key:Q", "move:1,0", "move:-1,0", "move:0,1", "move:0,-1", "confirm:Left", "confirm:Right", "backspace", "cancel" },
                events);
        });
    }

    [Fact]
    public void PrimaryOverlay_ConsumesRepeatedAndUnmappedKeysWithoutDispatching()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            int dispatchCount = 0;
            handler.KeyPressed += _ => dispatchCount++;
            handler.ConfirmRequested += _ => dispatchCount++;

            Assert.True(handler.HandleKeyDown(Key.Q, ModifierKeys.None, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.Enter, ModifierKeys.Shift, isRepeat: true));
            Assert.True(handler.HandleKeyUp(Key.Enter, ModifierKeys.Shift));
            Assert.True(handler.HandleKeyDown(Key.F1, ModifierKeys.None, isRepeat: false));

            Assert.Equal(0, dispatchCount);
        });
    }

    [Fact]
    public void PrimaryOverlay_UsesModifiersCapturedOnEnterKeyDown()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            ClickKind? confirmed = null;
            handler.ConfirmRequested += clickKind => confirmed = clickKind;

            Assert.True(handler.HandleKeyDown(Key.Enter, ModifierKeys.Shift, isRepeat: false));
            // The user may release Shift before releasing Enter.
            Assert.True(handler.HandleKeyUp(Key.Enter, ModifierKeys.None));

            Assert.Equal(ClickKind.Right, confirmed);
        });
    }

    [Fact]
    public void SecondaryOverlay_DoesNotHandleOrDispatchKeyboardInput()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: false);
            int dispatchCount = 0;
            handler.KeyPressed += _ => dispatchCount++;

            Assert.False(handler.HandleKeyDown(Key.Q, ModifierKeys.None, isRepeat: false));
            Assert.False(handler.HandleKeyDown(Key.Q, ModifierKeys.None, isRepeat: true));
            Assert.False(handler.HandleKeyUp(Key.Enter, ModifierKeys.None));

            Assert.Equal(0, dispatchCount);
        });
    }
}
