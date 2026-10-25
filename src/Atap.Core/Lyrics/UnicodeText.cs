using System.Text;

namespace Atap.Core.Lyrics;

internal static class UnicodeText
{
    /// <summary>
    /// Returns <paramref name="text"/> in Unicode form C, so Hangul typed as separate letters and kana with a separate
    /// voicing mark match the romanization tables. Text with broken UTF-16 is returned as it is.
    /// </summary>
    public static string Compose(string text)
    {
        if (Ascii.IsValid(text))
            return text;

        try
        {
            return text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }
}
