using InvoiceApp.Domain;

namespace InvoiceApp.Tests;

/// <summary>
/// Repository whose <see cref="Update"/> signals that a decision is being saved and then waits
/// until the test releases it, so a test can start a second decision while the first is in flight.
/// </summary>
internal class BlockingUpdateRepository : IInvoiceRepository
{
    private readonly FakeInvoiceRepository _inner;

    public BlockingUpdateRepository(params Invoice[] seed) => _inner = new FakeInvoiceRepository(seed);

    public ManualResetEventSlim UpdateEntered { get; } = new(false);
    public ManualResetEventSlim Release { get; } = new(false);

    public void Update(Invoice invoice)
    {
        UpdateEntered.Set();
        Release.Wait(TimeSpan.FromSeconds(10));
        _inner.Update(invoice);
    }

    public IReadOnlyList<Invoice> GetAll() => _inner.GetAll();
    public Invoice? GetById(string id) => _inner.GetById(id);
    public IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids) => _inner.GetByIds(ids);
    public Invoice Add(Invoice invoice) => _inner.Add(invoice);
    public bool Remove(string id) => _inner.Remove(id);
}
