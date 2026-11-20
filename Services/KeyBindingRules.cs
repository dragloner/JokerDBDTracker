using System.Windows.Input;

namespace JokerDBDTracker.Services;

public static class KeyBindingRules
{
    public static bool IsAllowed(Key key) => Enum.IsDefined(key) && key is not
        (Key.None or Key.M or Key.Escape or Key.Tab or Key.LeftShift or Key.RightShift or
         Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or
         Key.Space or Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.System or Key.ImeProcessed or Key.DeadCharProcessed);

    public static Key Canonical(Key key) => key is >= Key.NumPad0 and <= Key.NumPad9 ? Key.D0 + (key - Key.NumPad0) : key;
}
