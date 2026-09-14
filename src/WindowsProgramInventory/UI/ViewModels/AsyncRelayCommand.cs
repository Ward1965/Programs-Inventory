using System.Windows.Input;

namespace WindowsProgramInventory.UI.ViewModels;

/// <summary>
/// Async ICommand with cancellation support. Never blocks the UI thread.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    public AsyncRelayCommand(
        Func<CancellationToken, Task> execute,
        Func<object?, bool>? canExecute = null,
        Action<Exception>? onError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _isRunning;

    public bool CanExecute(object? parameter)
    {
        if (_isRunning)
        {
            return false;
        }

        return _canExecute?.Invoke(parameter) ?? true;
    }

    public void Execute(object? parameter) => _ = ExecuteAsync(parameter, CancellationToken.None);

    public async Task ExecuteAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when the user cancels a scan.
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}