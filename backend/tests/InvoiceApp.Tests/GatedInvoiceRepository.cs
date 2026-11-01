using InvoiceApp.Domain;

namespace InvoiceApp.Tests;

/// <summary>
/// Repository for concurrency tests. <see cref="GetById"/> hands out the invoice and then waits
/// (briefly) until every expected caller has fetched it, so unsynchronised code lets all callers
/// see the invoice as still pending. Code that serialises the whole decision never has two
/// callers inside <see cref="GetById"/> together, so each caller simply times out and proceeds.
/// </summary>
internal class GatedInvoiceRepository : IInvoiceRepository
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromMilliseconds(150);

    private readonly FakeInvoiceRepository _inner;
    private readonly int _participants;
    private readonly ManualResetEventSlim _gate = new(false);
    private int _arrived;

    public GatedInvoiceRepository(int participants, params Invoice[] seed)
    {
        _participants = participants;
        _inner = new FakeInvoiceRepository(seed);
    }

    public Invoice? GetById(string id)
    {
        var invoice = _inner.GetById(id);
        if (Interlocked.Increment(ref _arrived) == _participants)
        {
            _gate.Set();
        }

        _gate.Wait(GateTimeout);
        return invoice;
    }

    public IReadOnlyList<Invoice> GetAll() => _inner.GetAll();
    public IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids) => _inner.GetByIds(ids);
    public Invoice Add(Invoice invoice) => _inner.Add(invoice);
    public void Update(Invoice invoice) => _inner.Update(invoice);
    public bool Remove(string id) => _inner.Remove(id);
}
