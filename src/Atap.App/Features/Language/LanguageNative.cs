using System.Globalization;
using System.Runtime.InteropServices;

namespace Atap.Features.Language;

/// <summary>The Windows display language. Atap sets its own UI culture at startup, so this asks Windows directly.</summary>
internal static class LanguageNative
{
    public static CultureInfo WindowsDisplayCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(GetUserDefaultUILanguage());
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InstalledUICulture;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();
}
