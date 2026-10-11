namespace Skylab.Forms.Infrastructure.Turnstile;

public sealed class TurnstileReachability
{
    private static readonly TimeSpan ReachableFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UnreachableFor = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private bool _reachable;
    private DateTime _validUntil = DateTime.MinValue;

    public SemaphoreSlim Probe { get; } = new(1, 1);

    public bool? Current
    {
        get
        {
            lock (_gate)
            {
                return DateTime.UtcNow < _validUntil ? _reachable : null;
            }
        }
    }

    public void Record(bool reachable)
    {
        lock (_gate)
        {
            _reachable = reachable;
            _validUntil = DateTime.UtcNow + (reachable ? ReachableFor : UnreachableFor);
        }
    }
}
