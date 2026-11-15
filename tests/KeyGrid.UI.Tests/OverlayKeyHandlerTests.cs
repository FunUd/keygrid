using System.Windows.Input;
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
            handler.EnterPressed += () => events.Add("enter");
            handler.BackspacePressed += () => events.Add("backspace");
            handler.CancelRequested += () => events.Add("cancel");

            Assert.True(handler.HandleKeyDown(Key.Q, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Right, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Left, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Down, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Up, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Enter, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Back, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Escape, isRepeat: false));

            Assert.Equal(
                new[] { "key:Q", "move:1,0", "move:-1,0", "move:0,1", "move:0,-1", "enter", "backspace", "cancel" },
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
            handler.EnterPressed += () => dispatchCount++;

            Assert.True(handler.HandleKeyDown(Key.Q, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.F1, isRepeat: false));

            Assert.Equal(0, dispatchCount);
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

            Assert.False(handler.HandleKeyDown(Key.Q, isRepeat: false));
            Assert.False(handler.HandleKeyDown(Key.Q, isRepeat: true));

            Assert.Equal(0, dispatchCount);
        });
    }
}
