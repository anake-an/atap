using System.Windows.Input;

namespace Atap.Notch;

/// <summary>One entry in a right-click menu, with a check mark when it is the current choice.</summary>
public sealed record MenuOptionViewModel(string Header, bool IsChecked, ICommand? Command, bool IsEnabled = true);
