namespace KeyGrid.Core.Abstractions;

public interface IHotkeyRegistrar
{
    bool Register(nint hWnd, int id, uint modifiers, uint vk);
    bool Unregister(nint hWnd, int id);
}
