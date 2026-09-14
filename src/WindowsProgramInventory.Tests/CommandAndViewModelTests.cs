using WindowsProgramInventory.UI.ViewModels;

namespace WindowsProgramInventory.Tests;

public class ViewModelBaseTests
{
    private sealed class Probe : ViewModelBase
    {
        private string? _value;

        public string? Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }
    }

    [Fact]
    public void SetProperty_RaisesChange()
    {
        var probe = new Probe();
        string? seen = null;
        probe.PropertyChanged += (_, e) => seen = e.PropertyName;

        probe.Value = "x";

        Assert.Equal(nameof(probe.Value), seen);
    }

    [Fact]
    public void SetProperty_SameValue_DoesNotRaise()
    {
        var probe = new Probe { Value = "same" };
        var raised = 0;
        probe.PropertyChanged += (_, _) => raised++;

        probe.Value = "same";

        Assert.Equal(0, raised);
    }
}

public class RelayCommandTests
{
    [Fact]
    public void Execute_InvokesAction()
    {
        var ran = false;
        var cmd = new RelayCommand(_ => ran = true);
        cmd.Execute(null);
        Assert.True(ran);
    }

    [Fact]
    public void CanExecute_RespectsGate()
    {
        var invoked = 0;
        var cmd = new RelayCommand(_ => invoked++, _ => false);
        Assert.False(cmd.CanExecute(null));

        cmd.Execute(null);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public void RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new RelayCommand(_ => { });
        var fired = 0;
        cmd.CanExecuteChanged += (_, _) => fired++;

        cmd.RaiseCanExecuteChanged();

        Assert.Equal(1, fired);
    }
}

public class AsyncRelayCommandTests
{
    [Fact]
    public async Task Execute_RunsAndCompletes()
    {
        var ran = false;
        var cmd = new AsyncRelayCommand(async _ =>
        {
            await Task.Delay(20);
            ran = true;
        });

        await cmd.ExecuteAsync(null, CancellationToken.None);

        Assert.True(ran);
        Assert.False(cmd.IsRunning);
        Assert.True(cmd.CanExecute(null));
    }

    [Fact]
    public async Task CanExecute_False_WhileRunning()
    {
        var release = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand(_ => release.Task);

        var run = cmd.ExecuteAsync(null, CancellationToken.None);

        Assert.True(cmd.IsRunning);
        Assert.False(cmd.CanExecute(null));

        release.SetResult();
        await run;
        Assert.True(cmd.CanExecute(null));
    }

    [Fact]
    public async Task Cancellation_CompletesWithoutThrow()
    {
        using var cts = new CancellationTokenSource();
        var cmd = new AsyncRelayCommand(_ => Task.Delay(Timeout.Infinite, cts.Token));

        var run = cmd.ExecuteAsync(null, cts.Token);
        cts.Cancel();

        await run; // must not throw
    }
}