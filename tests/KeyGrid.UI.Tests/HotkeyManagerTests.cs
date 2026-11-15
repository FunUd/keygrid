using KeyGrid.Core.Abstractions;
using KeyGrid.Interop;
using KeyGrid.Services;

namespace KeyGrid.UI.Tests;

public class HotkeyManagerTests
{
    [Fact]
    public void RegisterDefaultHotkey_UsesCtrlShiftSpaceWithoutRepeat()
    {
        var registrar = new FakeHotkeyRegistrar();
        var manager = new HotkeyManager(registrar, new nint(42));

        Assert.True(manager.RegisterDefaultHotkey());

        Assert.True(manager.IsRegistered);
        Assert.Equal(new nint(42), registrar.RegisteredWindow);
        Assert.Equal(HotkeyManager.DefaultHotkeyId, registrar.RegisteredId);
        Assert.Equal(
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            registrar.RegisteredModifiers);
        Assert.Equal(NativeMethods.VK_SPACE, registrar.RegisteredVirtualKey);
    }

    [Fact]
    public void RegisterFailure_LeavesManagerUnregisteredAndDoesNotUnregisterOnDispose()
    {
        var registrar = new FakeHotkeyRegistrar { RegisterResult = false };
        var manager = new HotkeyManager(registrar, nint.Zero);

        Assert.False(manager.RegisterDefaultHotkey());
        manager.Dispose();

        Assert.False(manager.IsRegistered);
        Assert.Equal(0, registrar.UnregisterCalls);
    }

    [Fact]
    public void HandleHotkeyMessage_TriggersOnlyForRegisteredHotkeyId()
    {
        var manager = new HotkeyManager(new FakeHotkeyRegistrar(), nint.Zero);
        int triggerCount = 0;
        manager.HotkeyTriggered += () => triggerCount++;

        manager.HandleHotkeyMessage(HotkeyManager.DefaultHotkeyId + 1);
        Assert.Equal(0, triggerCount);

        manager.HandleHotkeyMessage(HotkeyManager.DefaultHotkeyId);
        Assert.Equal(1, triggerCount);
    }

    [Fact]
    public void Dispose_UnregistersRegisteredHotkeyOnlyOnce()
    {
        var registrar = new FakeHotkeyRegistrar();
        var manager = new HotkeyManager(registrar, new nint(42));
        Assert.True(manager.RegisterDefaultHotkey());

        manager.Dispose();
        manager.Dispose();

        Assert.Equal(1, registrar.UnregisterCalls);
        Assert.Equal(new nint(42), registrar.UnregisteredWindow);
        Assert.Equal(HotkeyManager.DefaultHotkeyId, registrar.UnregisteredId);
        Assert.False(manager.IsRegistered);
    }

    private sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
    {
        public bool RegisterResult { get; set; } = true;
        public nint RegisteredWindow { get; private set; }
        public int RegisteredId { get; private set; }
        public uint RegisteredModifiers { get; private set; }
        public uint RegisteredVirtualKey { get; private set; }
        public nint UnregisteredWindow { get; private set; }
        public int UnregisteredId { get; private set; }
        public int UnregisterCalls { get; private set; }

        public bool Register(nint hWnd, int id, uint modifiers, uint vk)
        {
            RegisteredWindow = hWnd;
            RegisteredId = id;
            RegisteredModifiers = modifiers;
            RegisteredVirtualKey = vk;
            return RegisterResult;
        }

        public bool Unregister(nint hWnd, int id)
        {
            UnregisteredWindow = hWnd;
            UnregisteredId = id;
            UnregisterCalls++;
            return true;
        }
    }
}
