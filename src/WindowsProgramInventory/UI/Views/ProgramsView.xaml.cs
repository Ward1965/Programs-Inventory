using System.Windows;
using System.Windows.Controls;
using WindowsProgramInventory.UI.ViewModels;

namespace WindowsProgramInventory.UI.Views;

public partial class ProgramsView : UserControl
{
    public ProgramsView()
    {
        InitializeComponent();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is ProgramsViewModel vm)
        {
            vm.UpdateViewport(e.ViewportWidth, e.ViewportHeight);
            vm.OnScroll(e.VerticalOffset, e.ViewportHeight, e.ExtentHeight);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ProgramsViewModel vm)
        {
            vm.UpdateViewport(e.NewSize.Width, e.NewSize.Height);
        }
    }
}