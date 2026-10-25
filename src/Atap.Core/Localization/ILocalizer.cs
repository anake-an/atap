using System.ComponentModel;
using System.Globalization;

namespace Atap.Core.Localization;

/// <summary>
/// UI text in English or Bahasa Melayu. Keys start with their area, for example "clock.name".
/// The indexer raises <see cref="INotifyPropertyChanged.PropertyChanged"/> when the language changes,
/// so bound text updates without a restart.
/// </summary>
public interface ILocalizer : INotifyPropertyChanged
{
    /// <summary>The text for <paramref name="key"/>. Falls back to English, then to the key itself.</summary>
    string this[string key] { get; }

    /// <summary>Fills named placeholders: <c>"in {minutes} min"</c> with ("minutes", 23).</summary>
    string Format(string key, params (string Name, object? Value)[] args);

    /// <summary>"en" or "ms".</summary>
    string Language { get; }

    /// <summary>en-MY or ms-MY. Used for day names, month names and numbers in text.</summary>
    CultureInfo Culture { get; }

    /// <summary>Raised on the UI thread after the language changed.</summary>
    event EventHandler? LanguageChanged;
}
