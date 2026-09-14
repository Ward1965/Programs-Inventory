using System.Windows;
using WindowsProgramInventory.Core.Interfaces;

namespace WindowsProgramInventory.Startup;

/// <summary>
/// WPF MessageBox-backed dialog service. Only used for user-facing interactions.
/// </summary>
public sealed class DialogService : IDialogService
{
    private Window? Owner => Application.Current?.MainWindow;

    public bool Confirm(string message, string title)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowInfo(string message, string title)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string message, string title)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}