namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Abstraction over user dialogs so ViewModels stay testable without a UI host.
/// </summary>
public interface IDialogService
{
    bool Confirm(string message, string title);
    void ShowInfo(string message, string title);
    void ShowError(string message, string title);
}