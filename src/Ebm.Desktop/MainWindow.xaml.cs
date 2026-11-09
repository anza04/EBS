using System.Windows;
using Ebm.Desktop.ViewModels;

namespace Ebm.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.Editor.ReloadAsync();
    }
}
