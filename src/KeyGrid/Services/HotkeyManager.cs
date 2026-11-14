using KeyGrid.Core.Abstractions;
using KeyGrid.Interop;

namespace KeyGrid.Services;

/// <summary>
/// Registers and manages global hotkey triggers according to spec.md Section 7.2.
/// </summary>
public class HotkeyManager : IDisposable
{
    public const int DefaultHotkeyId = 9001;

    private readonly IHotkeyRegistrar _registrar;
    private readonly nint _hWnd;
    private bool _registered = false;

    public event Action? HotkeyTriggered;

    public bool IsRegistered => _registered;

    public HotkeyManager(IHotkeyRegistrar registrar, nint hWnd)
    {
        _registrar = registrar;
        _hWnd = hWnd;
    }

    public bool RegisterDefaultHotkey()
    {
        uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
        uint vk = NativeMethods.VK_SPACE;

        _registered = _registrar.Register(_hWnd, DefaultHotkeyId, modifiers, vk);
        return _registered;
    }

    public void HandleHotkeyMessage(int id)
    {
        if (id == DefaultHotkeyId)
        {
            HotkeyTriggered?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            _registrar.Unregister(_hWnd, DefaultHotkeyId);
            _registered = false;
        }
    }
}
