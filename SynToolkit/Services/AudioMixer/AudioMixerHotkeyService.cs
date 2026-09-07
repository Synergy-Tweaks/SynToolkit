#nullable enable

using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace SynToolkit.Services.AudioMixer
{
    public sealed class AudioMixerHotkeyService : IDisposable
    {
        public const uint ModAlt = 0x1;
        public const uint ModControl = 0x2;
        public const uint ModShift = 0x4;
        public const uint ModWin = 0x8;
        private const uint ModNoRepeat = 0x4000;
        private const int WmHotkey = 0x0312;
        private const uint HotkeySubclassId = 0x1D6E;
        private const int HotkeyId = 0x1D6E;

        private readonly AudioMixerSettingsStore _settingsStore;
        private IntPtr _windowHandle;
        private bool _subclassInstalled;
        private bool _registered;
        private bool _disposed;
        private SUBCLASSPROC? _subclassProc;

        public event Action? HotkeyPressed;

        public AudioMixerHotkeyService(AudioMixerSettingsStore settingsStore)
        {
            _settingsStore = settingsStore;
        }

        public AudioMixerHotkey CurrentHotkey { get; private set; } = AudioMixerHotkey.Default;

        public void Initialize(Window window)
        {
            if (_disposed)
            {
                return;
            }

            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (_windowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to get the main window handle for audio mixer hotkeys.");
            }

            if (!_subclassInstalled)
            {
                _subclassProc = WndProc;
                if (!SetWindowSubclass(_windowHandle, _subclassProc, HotkeySubclassId, IntPtr.Zero))
                {
                    throw new InvalidOperationException("Unable to install the audio mixer hotkey window hook.");
                }

                _subclassInstalled = true;
            }

            AudioMixerHotkey savedHotkey = _settingsStore.GetHotkey();
            if (!TryApplyHotkey(savedHotkey))
            {
                TryApplyHotkey(AudioMixerHotkey.Default);
            }
        }

        public bool TryUpdateHotkey(AudioMixerHotkey hotkey)
        {
            if (_disposed)
            {
                return false;
            }

            if (!hotkey.HasModifier || hotkey.VirtualKey == 0)
            {
                return false;
            }

            if (!TryApplyHotkey(hotkey))
            {
                return false;
            }

            _settingsStore.SetHotkey(hotkey);
            return true;
        }

        public static string Describe(AudioMixerHotkey hotkey)
        {
            List<string> parts = [];
            if ((hotkey.Modifiers & ModControl) != 0)
            {
                parts.Add("Ctrl");
            }

            if ((hotkey.Modifiers & ModShift) != 0)
            {
                parts.Add("Shift");
            }

            if ((hotkey.Modifiers & ModAlt) != 0)
            {
                parts.Add("Alt");
            }

            if ((hotkey.Modifiers & ModWin) != 0)
            {
                parts.Add("Win");
            }

            parts.Add(DescribeVirtualKey(hotkey.VirtualKey));
            return string.Join(" + ", parts);
        }

        public static string DescribeVirtualKey(uint virtualKey)
        {
            if (virtualKey >= 0x41 && virtualKey <= 0x5A)
            {
                return ((char)virtualKey).ToString();
            }

            if (virtualKey >= 0x30 && virtualKey <= 0x39)
            {
                return ((char)virtualKey).ToString();
            }

            return virtualKey switch
            {
                0x70 => "F1",
                0x71 => "F2",
                0x72 => "F3",
                0x73 => "F4",
                0x74 => "F5",
                0x75 => "F6",
                0x76 => "F7",
                0x77 => "F8",
                0x78 => "F9",
                0x79 => "F10",
                0x7A => "F11",
                0x7B => "F12",
                _ => "Unknown"
            };
        }

        private bool TryApplyHotkey(AudioMixerHotkey hotkey)
        {
            Unregister();
            if (_windowHandle == IntPtr.Zero)
            {
                return false;
            }

            _registered = RegisterHotKey(_windowHandle, HotkeyId, hotkey.Modifiers | ModNoRepeat, hotkey.VirtualKey);
            if (_registered)
            {
                CurrentHotkey = hotkey;
            }

            return _registered;
        }

        private void Unregister()
        {
            if (_registered && _windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HotkeyId);
                _registered = false;
            }
        }

        private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
        {
            if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke();
                return 0;
            }

            return DefSubclassProc(hwnd, message, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Unregister();

            if (_subclassInstalled && _windowHandle != IntPtr.Zero && _subclassProc is not null)
            {
                RemoveWindowSubclass(_windowHandle, _subclassProc, HotkeySubclassId);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

        private delegate nint SUBCLASSPROC(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData);
    }
}
