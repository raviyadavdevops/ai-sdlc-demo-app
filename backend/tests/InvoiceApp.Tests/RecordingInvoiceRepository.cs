using InvoiceApp.Domain;

namespace InvoiceApp.Tests;

/// <summary>
/// Same behaviour as <see cref="FakeInvoiceRepository"/>, but remembers which invoices were
/// saved so tests can prove a rejected attempt never reaches the repository.
/// </summary>
internal class RecordingInvoiceRepository : IInvoiceRepository
{
    private readonly FakeInvoiceRepository _inner;

    public RecordingInvoiceRepository(params Invoice[] seed) => _inner = new FakeInvoiceRepository(seed);

    public List<string> UpdatedInvoiceIds { get; } = new();

    public IReadOnlyList<Invoice> GetAll() => _inner.GetAll();
    public Invoice? GetById(string id) => _inner.GetById(id);
    public IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids) => _inner.GetByIds(ids);
    public Invoice Add(Invoice invoice) => _inner.Add(invoice);
    public bool Remove(string id) => _inner.Remove(id);

    public void Update(Invoice invoice)
    {
        UpdatedInvoiceIds.Add(invoice.Id);
        _inner.Update(invoice);
    }
}
