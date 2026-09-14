using System.Windows;

namespace WindowsProgramInventory.UI;

/// <summary>
/// In Phase 1 the window is a thin shell: all behavior is driven by the DataContext
/// (MainViewModel). Code-behind is intentionally empty and stays that way if possible.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}