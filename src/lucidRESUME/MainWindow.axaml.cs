using Avalonia.Controls;
using lucidRESUME.ViewModels;

namespace lucidRESUME;

public partial class MainWindow : Window
{
    // Parameterless constructor for Avalonia designer / XAML runtime loader
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }
}
