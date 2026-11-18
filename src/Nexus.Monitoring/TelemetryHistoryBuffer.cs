using Nexus.Models;

namespace Nexus.Monitoring;

public sealed class TelemetryHistoryBuffer
{
    private readonly int _capacity;
    private readonly LinkedList<SystemSnapshot> _items = new();
    private readonly Lock _sync = new();

    public TelemetryHistoryBuffer(int capacity = 3600)
    {
        _capacity = Math.Max(60, capacity);
    }

    public int Count
    {
        get
        {
            lock (_sync) return _items.Count;
        }
    }

    public void Add(SystemSnapshot snapshot)
    {
        lock (_sync)
        {
            _items.AddLast(snapshot);
            while (_items.Count > _capacity)
            {
                _items.RemoveFirst();
            }
        }
    }

    public IReadOnlyList<SystemSnapshot> GetWindow(TimeSpan window)
    {
        lock (_sync)
        {
            if (_items.Count == 0) return [];
            if (window == TimeSpan.MaxValue) return [.. _items];

            var cutoff = DateTimeOffset.UtcNow - window;
            return _items.Where(s => s.TimestampUtc >= cutoff).ToList();
        }
    }
}

