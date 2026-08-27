using StockTvKiosk.Core.Control;

namespace StockTvKiosk.Core.Tests;

/// <summary>
/// Nimmt Befehle entgegen, statt sie auszufuehren - andernfalls wuerden die Tests den
/// Testrechner neu starten.
/// </summary>
internal sealed class FakeCommandRunner : ICommandRunner
{
    private readonly TaskCompletionSource _executed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<CommandSpec> Executed { get; } = [];

    /// <summary>Was der Aufruf melden soll. Vorbelegt mit Erfolg.</summary>
    public CommandOutcome Outcome { get; set; } = new(0, string.Empty, string.Empty, TimedOut: false);

    public Task<CommandOutcome> RunAsync(
        CommandSpec command, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        lock (Executed)
            Executed.Add(command);

        _executed.TrySetResult();
        return Task.FromResult(Outcome);
    }

    /// <summary>Wartet, bis ein Befehl ankam. Liefert false, wenn innerhalb der Frist keiner kam.</summary>
    public async Task<bool> WaitForExecutionAsync(TimeSpan timeout)
    {
        var finished = await Task.WhenAny(_executed.Task, Task.Delay(timeout));
        return finished == _executed.Task;
    }
}
