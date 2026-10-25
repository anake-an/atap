using System.Windows;
using Atap.Platform.Diagnostics;

namespace Atap.Features.Onboarding;

internal partial class OnboardingWindow : Window, IOnboardingWindow
{
    private OnboardingWindow(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Closed += (_, _) =>
        {
            viewModel.Dispose();
            MemoryTrim.Soon(Dispatcher);
        };
    }

    public static IOnboardingWindow Open(OnboardingViewModel viewModel)
    {
        var window = new OnboardingWindow(viewModel);
        window.Show();
        window.Activate();
        return window;
    }
}
