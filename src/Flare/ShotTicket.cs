namespace Flare;

// Claim the exact firing item once, before processing potentially reentrant RPCs.
public sealed class ShotTicket<T> where T : class
{
    public readonly string Id;
    public readonly double Deadline;
    private readonly T item;
    private bool claimed;
    public ShotTicket(string id, T item, double deadline) { Id = id; this.item = item; Deadline = deadline; }
    public T? Claim(string id, double now)
    {
        if (claimed || id != Id || now >= Deadline) return null;
        claimed = true;
        return item;
    }
}
