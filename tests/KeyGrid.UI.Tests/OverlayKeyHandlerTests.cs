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
            Assert.True(handler.HandleKeyDown(Key.Enter, ModifierKeys.Control, isRepeat: false));
            Assert.True(handler.HandleKeyUp(Key.Enter, ModifierKeys.Control));
            foreach (var modifiers in new[]
            {
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
                new[] { "key:Q", "move:1,0", "move:-1,0", "move:0,1", "move:0,-1", "confirm:Left", "confirm:Right", "confirm:Middle", "backspace", "cancel" },
                events);
        });
    }

    [Fact]
    public void PrimaryOverlay_RepeatedArrowsDispatchOnlyWhenRepeatIsEnabled()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            var moves = new List<string>();
            handler.MoveRequested += (column, row) => moves.Add($"{column},{row}");

            Assert.True(handler.HandleKeyDown(Key.Right, ModifierKeys.None, isRepeat: true));
            Assert.Empty(moves);

            handler.AllowArrowKeyRepeat = true;
            Assert.True(handler.HandleKeyDown(Key.Right, ModifierKeys.None, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.Up, ModifierKeys.None, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.Q, ModifierKeys.None, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.PageDown, ModifierKeys.None, isRepeat: true));

            Assert.Equal(new[] { "1,0", "0,-1" }, moves);
        });
    }

    [Fact]
    public void PrimaryOverlay_PlainDRequestsDragStart()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            int dragStartCount = 0;
            handler.DragStartRequested += () => dragStartCount++;

            Assert.True(handler.HandleKeyDown(Key.D, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.D, ModifierKeys.None, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.D, ModifierKeys.Shift, isRepeat: false));

            Assert.Equal(1, dragStartCount);
        });
    }

    [Fact]
    public void PrimaryOverlay_UnmodifiedRRequestsRangeStart()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            int rangeStartCount = 0;
            var pressedKeys = new List<char>();
            handler.RangeStartRequested += () => rangeStartCount++;
            handler.KeyPressed += key => pressedKeys.Add(key);

            Assert.True(handler.HandleKeyDown(Key.R, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.R, ModifierKeys.None, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.R, ModifierKeys.Shift, isRepeat: false));

            Assert.Equal(1, rangeStartCount);
            Assert.Equal(new[] { 'R' }, pressedKeys);
        });
    }

    [Theory]
    [InlineData(Key.R, Key.D)]
    [InlineData(Key.D, Key.R)]
    public void PrimaryOverlay_OverlappingRAndDRequestsWorkflowWithoutIndividualActions(
        Key firstKey,
        Key secondKey)
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true)
            {
                AllowRangeDragChord = true
            };
            int rangeCount = 0;
            int dragCount = 0;
            int workflowCount = 0;
            handler.RangeStartRequested += () => rangeCount++;
            handler.DragStartRequested += () => dragCount++;
            handler.RangeDragWorkflowRequested += () => workflowCount++;

            Assert.True(handler.HandleKeyDown(firstKey, ModifierKeys.None, isRepeat: false));
            Assert.Equal(0, workflowCount);
            Assert.True(handler.HandleKeyDown(secondKey, ModifierKeys.None, isRepeat: false));
            Assert.Equal(1, workflowCount);

            Assert.True(handler.HandleKeyUp(secondKey, ModifierKeys.None));
            Assert.True(handler.HandleKeyUp(firstKey, ModifierKeys.None));
            Assert.Equal(0, rangeCount);
            Assert.Equal(0, dragCount);
            Assert.Equal(1, workflowCount);
        });
    }

    [Theory]
    [InlineData(Key.R)]
    [InlineData(Key.D)]
    public void PrimaryOverlay_EligibleSingleRangeOrDragKeyDispatchesOnKeyUp(Key key)
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true)
            {
                AllowRangeDragChord = true
            };
            int rangeCount = 0;
            int dragCount = 0;
            handler.RangeStartRequested += () => rangeCount++;
            handler.DragStartRequested += () => dragCount++;

            Assert.True(handler.HandleKeyDown(key, ModifierKeys.None, isRepeat: false));
            Assert.Equal(0, rangeCount + dragCount);
            Assert.True(handler.HandleKeyUp(key, ModifierKeys.None));

            Assert.Equal(key == Key.R ? 1 : 0, rangeCount);
            Assert.Equal(key == Key.D ? 1 : 0, dragCount);
        });
    }

    [Fact]
    public void PrimaryOverlay_ModifierKeyBetweenChordKeysDoesNotBreakOverlap()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true)
            {
                AllowRangeDragChord = true
            };
            int workflowCount = 0;
            handler.RangeDragWorkflowRequested += () => workflowCount++;

            Assert.True(handler.HandleKeyDown(Key.R, ModifierKeys.None, isRepeat: false));
            // A brushed Alt key-down must not cancel the pending chord key.
            Assert.True(handler.HandleKeyDown(Key.LeftAlt, ModifierKeys.Alt, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.D, ModifierKeys.None, isRepeat: false));

            Assert.Equal(1, workflowCount);
        });
    }

    [Fact]
    public void PrimaryOverlay_FRequestsWorkflowOnlyWhenChordIsEligible()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true)
            {
                AllowRangeDragChord = true
            };
            int workflowCount = 0;
            var pressedKeys = new List<char>();
            handler.RangeDragWorkflowRequested += () => workflowCount++;
            handler.KeyPressed += key => pressedKeys.Add(key);

            Assert.True(handler.HandleKeyDown(Key.F, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.F, ModifierKeys.Shift, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.F, ModifierKeys.None, isRepeat: true));

            Assert.Equal(1, workflowCount);
            Assert.Equal(new[] { 'F' }, pressedKeys);

            // Outside eligible Stage 2 state F stays an ordinary letter key.
            handler.AllowRangeDragChord = false;
            Assert.True(handler.HandleKeyDown(Key.F, ModifierKeys.None, isRepeat: false));
            Assert.Equal(2, pressedKeys.Count);
            Assert.Equal('F', pressedKeys[1]);
        });
    }

    [Fact]
    public void PrimaryOverlay_CancelClearsPendingRangeDragKey()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true)
            {
                AllowRangeDragChord = true
            };
            int rangeCount = 0;
            handler.RangeStartRequested += () => rangeCount++;

            Assert.True(handler.HandleKeyDown(Key.R, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Escape, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyUp(Key.R, ModifierKeys.None));

            Assert.Equal(0, rangeCount);
        });
    }

    [Fact]
    public void PrimaryOverlay_PlainPageKeysRequestPageScroll()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            var directions = new List<int>();
            handler.PageScrollRequested += direction => directions.Add(direction);

            Assert.True(handler.HandleKeyDown(Key.PageUp, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.PageDown, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.PageDown, ModifierKeys.None, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.PageUp, ModifierKeys.Shift, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.PageDown, ModifierKeys.Control, isRepeat: false));

            Assert.Equal(new[] { -1, 1 }, directions);
        });
    }

    [Fact]
    public void PrimaryOverlay_PageRepeatsDispatchOnlyWhenRepeatIsEnabled()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            var directions = new List<int>();
            handler.PageScrollRequested += direction => directions.Add(direction);

            // Repeats stay ignored until Scroll Mode enables them.
            Assert.True(handler.HandleKeyDown(Key.PageDown, ModifierKeys.None, isRepeat: true));
            Assert.Empty(directions);

            handler.AllowPageScrollRepeat = true;
            Assert.True(handler.HandleKeyDown(Key.PageDown, ModifierKeys.None, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.PageUp, ModifierKeys.None, isRepeat: true));
            // Other repeated keys are still consumed without dispatching.
            Assert.True(handler.HandleKeyDown(Key.Q, ModifierKeys.None, isRepeat: true));

            Assert.Equal(new[] { 1, -1 }, directions);
        });
    }

    [Fact]
    public void PrimaryOverlay_TabTogglesContinuousModeAndIgnoresModifiedOrRepeatedTabs()
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            int toggleCount = 0;
            handler.ContinuousModeToggleRequested += () => toggleCount++;

            Assert.True(handler.HandleKeyDown(Key.Tab, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Tab, ModifierKeys.None, isRepeat: true));
            Assert.True(handler.HandleKeyDown(Key.Tab, ModifierKeys.Shift, isRepeat: false));
            Assert.True(handler.HandleKeyDown(Key.Tab, ModifierKeys.Control, isRepeat: false));
            Assert.False(handler.HandleKeyUp(Key.Tab, ModifierKeys.None));

            Assert.Equal(1, toggleCount);
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

    [Theory]
    [InlineData(ModifierKeys.Shift, ClickKind.Right)]
    [InlineData(ModifierKeys.Control, ClickKind.Middle)]
    public void PrimaryOverlay_UsesModifiersCapturedOnEnterKeyDown(
        ModifierKeys modifiers,
        ClickKind expected)
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            ClickKind? confirmed = null;
            handler.ConfirmRequested += clickKind => confirmed = clickKind;

            Assert.True(handler.HandleKeyDown(Key.Enter, modifiers, isRepeat: false));
            // The user may release the modifier before releasing Enter.
            Assert.True(handler.HandleKeyUp(Key.Enter, ModifierKeys.None));

            Assert.Equal(expected, confirmed);
        });
    }

    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.PageUp)]
    [InlineData(Key.PageDown)]
    public void PendingConfirmation_IsClearedByCancelOrPageKeys(Key interveningKey)
    {
        UiTestThread.Run(() =>
        {
            var handler = new OverlayKeyHandler(isPrimary: true);
            ClickKind? confirmed = null;
            handler.ConfirmRequested += clickKind => confirmed = clickKind;

            Assert.True(handler.HandleKeyDown(Key.Enter, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyDown(interveningKey, ModifierKeys.None, isRepeat: false));
            Assert.True(handler.HandleKeyUp(Key.Enter, ModifierKeys.None));

            Assert.Null(confirmed);
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
            handler.ContinuousModeToggleRequested += () => dispatchCount++;

            Assert.False(handler.HandleKeyDown(Key.Q, ModifierKeys.None, isRepeat: false));
            Assert.False(handler.HandleKeyDown(Key.Q, ModifierKeys.None, isRepeat: true));
            Assert.False(handler.HandleKeyDown(Key.Tab, ModifierKeys.None, isRepeat: false));
            Assert.False(handler.HandleKeyUp(Key.Enter, ModifierKeys.None));

            Assert.Equal(0, dispatchCount);
        });
    }
}
