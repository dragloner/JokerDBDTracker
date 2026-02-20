using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace JokerDBDTracker
{
    public partial class PlayerWindow : Window
    {
        private const int WmHotkey = 0x0312;
        private const uint ModNone = 0x0000;

        private void RegisterGlobalHotkeys()
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            _hotkeySource ??= HwndSource.FromHwnd(windowHandle);
            _hotkeySource?.RemoveHook(HotkeyWndProc);
            _hotkeySource?.AddHook(HotkeyWndProc);

            UnregisterGlobalHotkeys();
            foreach (var key in BuildPlayerHotkeySet())
            {
                RegisterSingleHotkey(windowHandle, key);
            }
        }

        private void PlayerWindow_Activated(object? sender, EventArgs e)
        {
            RegisterGlobalHotkeys();
        }

        private void PlayerWindow_Deactivated(object? sender, EventArgs e)
        {
            UnregisterGlobalHotkeys();
        }

        private void UnregisterGlobalHotkeys()
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle != IntPtr.Zero)
            {
                foreach (var (id, _) in _registeredHotkeys)
                {
                    UnregisterHotKey(windowHandle, id);
                }
            }

            _registeredHotkeys.Clear();
        }

        private IEnumerable<Key> BuildPlayerHotkeySet()
        {
            var keys = new HashSet<Key>
            {
                ReadConfiguredKey(_appSettings.HideEffectsPanelBind, Key.H),
                ReadConfiguredKey(_appSettings.AuraFarmSoundBind, Key.Y),
                ReadConfiguredKey(_appSettings.LaughSoundBind, Key.U),
                ReadConfiguredKey(_appSettings.PsiSoundBind, Key.I),
                ReadConfiguredKey(_appSettings.RespectSoundBind, Key.O),

                Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0,
                Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4, Key.NumPad5,
                Key.NumPad6, Key.NumPad7, Key.NumPad8, Key.NumPad9, Key.NumPad0,
                Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10,
                Key.F11, Key.F12,
                Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Z, Key.X, Key.C
            };

            return keys;
        }

        private void RegisterSingleHotkey(IntPtr windowHandle, Key key)
        {
            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey <= 0)
            {
                return;
            }

            var id = _nextHotkeyId++;
            if (RegisterHotKey(windowHandle, id, ModNone, (uint)virtualKey))
            {
                _registeredHotkeys[id] = key;
            }
        }

        private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmHotkey)
            {
                return IntPtr.Zero;
            }

            if (!_registeredHotkeys.TryGetValue(wParam.ToInt32(), out var key))
            {
                return IntPtr.Zero;
            }

            MarkUserInteraction();
            if (TryHandleAppKeybind(key))
            {
                handled = true;
            }

            return IntPtr.Zero;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
