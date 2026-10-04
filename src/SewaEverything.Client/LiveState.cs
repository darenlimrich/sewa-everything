using SewaEverything.Contracts;

namespace SewaEverything.Client;

public interface IPageVisibility
{
    ValueTask<bool> IsVisibleAsync();
}

public sealed class AlwaysVisible : IPageVisibility
{
    public ValueTask<bool> IsVisibleAsync() => ValueTask.FromResult(true);
}

public sealed class LiveState(SewaApi api, AuthState auth, IPageVisibility visibility) : IAsyncDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? loop;
    private PulseResponse? last;

    public PulseResponse? Pulse => last;

    public int Unread => last?.Unread ?? 0;
    public int ActionNeeded => last?.ActionNeeded ?? 0;
    public int CartCount => last?.CartCount ?? 0;
    public int OpenDisputes => last?.OpenDisputes ?? 0;
    public int PendingPayouts => last?.PendingPayouts ?? 0;
    public int PendingSellers => last?.PendingSellers ?? 0;
    public int PendingItems => last?.PendingItems ?? 0;

    public event Action? Changed;

    public void Start()
    {
        if (loop is not null)
        {
            return;
        }

        loop = new CancellationTokenSource();
        _ = RunAsync(loop.Token);
    }

    public async Task StopAsync()
    {
        if (loop is null)
        {
            return;
        }

        await loop.CancelAsync();
        loop.Dispose();
        loop = null;
        last = null;
    }

    public async Task RefreshAsync()
    {
        if (!auth.IsAuthenticated)
        {
            return;
        }

        await gate.WaitAsync();

        try
        {
            var pulse = await api.GetPulseAsync();

            if (pulse is null || pulse == last)
            {
                return;
            }

            last = pulse;
        }
        catch
        {
            return;
        }
        finally
        {
            gate.Release();
        }

        Changed?.Invoke();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);

        await RefreshAsync();

        while (await timer.WaitForNextTickAsync(ct))
        {
            if (!auth.IsAuthenticated)
            {
                continue;
            }

            if (!await visibility.IsVisibleAsync())
            {
                continue;
            }

            await RefreshAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        gate.Dispose();
    }
}
