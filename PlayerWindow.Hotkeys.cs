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
                ReadConfiguredKey(_appSettings.RespectSoundBind, Key.O)
            };

            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect1Bind, Key.D1));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect2Bind, Key.D2));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect3Bind, Key.D3));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect4Bind, Key.D4));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect5Bind, Key.D5));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect6Bind, Key.D6));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect7Bind, Key.D7));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect8Bind, Key.D8));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect9Bind, Key.D9));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect10Bind, Key.D0));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect11Bind, Key.Q));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect12Bind, Key.W));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect13Bind, Key.E));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect14Bind, Key.R));
            AddHotkeyWithDigitAliases(keys, ReadConfiguredKey(_appSettings.Effect15Bind, Key.T));
            return keys;
        }

        private static void AddHotkeyWithDigitAliases(HashSet<Key> keys, Key key)
        {
            keys.Add(key);
            if (!TryGetDigitAliasPair(key, out var topRow, out var numPad))
            {
                return;
            }

            keys.Add(topRow);
            keys.Add(numPad);
        }

        private static bool TryGetDigitAliasPair(Key key, out Key topRow, out Key numPad)
        {
            switch (key)
            {
                case Key.D0:
                case Key.NumPad0:
                    topRow = Key.D0;
                    numPad = Key.NumPad0;
                    return true;
                case Key.D1:
                case Key.NumPad1:
                    topRow = Key.D1;
                    numPad = Key.NumPad1;
                    return true;
                case Key.D2:
                case Key.NumPad2:
                    topRow = Key.D2;
                    numPad = Key.NumPad2;
                    return true;
                case Key.D3:
                case Key.NumPad3:
                    topRow = Key.D3;
                    numPad = Key.NumPad3;
                    return true;
                case Key.D4:
                case Key.NumPad4:
                    topRow = Key.D4;
                    numPad = Key.NumPad4;
                    return true;
                case Key.D5:
                case Key.NumPad5:
                    topRow = Key.D5;
                    numPad = Key.NumPad5;
                    return true;
                case Key.D6:
                case Key.NumPad6:
                    topRow = Key.D6;
                    numPad = Key.NumPad6;
                    return true;
                case Key.D7:
                case Key.NumPad7:
                    topRow = Key.D7;
                    numPad = Key.NumPad7;
                    return true;
                case Key.D8:
                case Key.NumPad8:
                    topRow = Key.D8;
                    numPad = Key.NumPad8;
                    return true;
                case Key.D9:
                case Key.NumPad9:
                    topRow = Key.D9;
                    numPad = Key.NumPad9;
                    return true;
                default:
                    topRow = Key.None;
                    numPad = Key.None;
                    return false;
            }
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
            if (ShouldSuppressDuplicateAppKeybind(key))
            {
                handled = true;
                return IntPtr.Zero;
            }

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
