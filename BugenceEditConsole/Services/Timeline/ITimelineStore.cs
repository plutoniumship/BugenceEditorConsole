public sealed record TimelineEvent(DateTimeOffset Timestamp, string Type, string Message, Guid? SectionId = null, string? User = null);


public interface ITimelineStore
{
    void Publish(Guid pageId, TimelineEvent evt);
    IReadOnlyList<TimelineEvent> GetRecent(Guid pageId, int take = 50);
    IAsyncEnumerable<TimelineEvent> Subscribe(Guid pageId, CancellationToken ct);
}


public sealed class TimelineMemoryStore : ITimelineStore
{
    private sealed class PageBus
    {
        public readonly System.Threading.Channels.Channel<TimelineEvent> Channel = System.Threading.Channels.Channel.CreateUnbounded<TimelineEvent>();
        public readonly LinkedList<TimelineEvent> Buffer = new();
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, PageBus> _buses = new();
    private const int MaxBuffers = 200;
    private PageBus Bus(Guid pageId) => _buses.GetOrAdd(pageId, _ => new PageBus());


    public void Publish(Guid pageId, TimelineEvent evt)
    {
        var bus = Bus(pageId);
        lock (bus.Buffer)
        {
            bus.Buffer.AddLast(evt);
            while (bus.Buffer.Count > MaxBuffers) bus.Buffer.RemoveFirst();
        }
        bus.Channel.Writer.TryWrite(evt);
    }


    public IReadOnlyList<TimelineEvent> GetRecent(Guid pageId, int take = 50)
    {
        var bus = Bus(pageId);
        lock (bus.Buffer)
        {
            return bus.Buffer
            .Reverse()
            .Take(take)
            .Reverse()
            .ToArray();
        }
    }


    public async IAsyncEnumerable<TimelineEvent> Subscribe(Guid pageId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var bus = Bus(pageId);
        TimelineEvent[] snapshot;
        lock (bus.Buffer)
            snapshot = bus.Buffer.TakeLast(20).ToArray();
        foreach (var e in snapshot)
            yield return e;


        var reader = bus.Channel.Reader;
        while (!ct.IsCancellationRequested)
        {
            TimelineEvent next;
            try { next = await reader.ReadAsync(ct); }
            catch (OperationCanceledException) { yield break; }
            yield return next;
        }
    }
}
