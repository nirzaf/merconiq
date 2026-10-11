using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Infrastructure.Services;

public sealed class PortalService(
    InventoryDbContext db,
    ITenantContext tenantContext) : IPortalService
{
    public async Task<CustomerPortalOverview> GetCustomerPortalOverviewAsync(
        int companyId,
        int customerId,
        string contactEmail,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = contactEmail.Trim().ToLowerInvariant();

        var contact = await db.PortalContacts.FirstOrDefaultAsync(
            c => c.CompanyId == companyId &&
                 c.TenantId == tenantContext.TenantId &&
                 c.PortalType == PortalType.Customer &&
                 c.PartyId == customerId &&
                 c.Email.ToLower() == normalizedEmail &&
                 c.IsActive,
            cancellationToken);

        if (contact is null)
            throw new UnauthorizedAccessException("Customer contact is not authorized for this portal account.");

        var customer = await db.Customers.FirstOrDefaultAsync(
            c => c.Id == customerId && c.CompanyId == companyId && c.TenantId == tenantContext.TenantId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Customer not found.");

        var orders = await db.SalesOrders
            .Include(o => o.Lines)
            .Where(o => o.CustomerId == customerId && o.CompanyId == companyId && o.TenantId == tenantContext.TenantId)
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new CustomerOrderSummary(
                o.Id,
                o.OrderNumber,
                o.OrderDate,
                "Approved",
                o.Lines.Sum(l => l.NetValue)))
            .ToListAsync(cancellationToken);

        var invoices = await db.SalesInvoices
            .Where(i => i.CustomerId == customerId && i.CompanyId == companyId && i.TenantId == tenantContext.TenantId)
            .OrderByDescending(i => i.InvoiceDate)
            .Select(i => new CustomerInvoiceSummary(
                i.Id,
                i.InvoiceNumber,
                i.InvoiceDate,
                "Posted",
                i.TotalValue,
                i.TotalValue))
            .ToListAsync(cancellationToken);

        var deliveries = await db.DeliveryNotes
            .Where(d => d.SalesOrder.CustomerId == customerId && d.CompanyId == companyId && d.TenantId == tenantContext.TenantId)
            .OrderByDescending(d => d.PostingDate)
            .Select(d => new CustomerDeliverySummary(
                d.Id,
                d.DeliveryNumber,
                d.PostingDate,
                "Posted"))
            .ToListAsync(cancellationToken);

        var totalOutstanding = invoices.Sum(i => i.OutstandingBalance);

        return new CustomerPortalOverview(
            customerId,
            customer.Name,
            totalOutstanding,
            orders,
            invoices,
            deliveries);
    }

    public async Task<SupplierPortalOverview> GetSupplierPortalOverviewAsync(
        int companyId,
        int supplierId,
        string contactEmail,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = contactEmail.Trim().ToLowerInvariant();

        var contact = await db.PortalContacts.FirstOrDefaultAsync(
            c => c.CompanyId == companyId &&
                 c.TenantId == tenantContext.TenantId &&
                 c.PortalType == PortalType.Supplier &&
                 c.PartyId == supplierId &&
                 c.Email.ToLower() == normalizedEmail &&
                 c.IsActive,
            cancellationToken);

        if (contact is null)
            throw new UnauthorizedAccessException("Supplier contact is not authorized for this portal account.");

        var supplier = await db.Suppliers.FirstOrDefaultAsync(
            s => s.Id == supplierId && s.TenantId == tenantContext.TenantId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Supplier not found.");

        var orders = await db.PurchaseOrders
            .Include(o => o.DocumentIdentity)
            .Where(o => o.SupplierId == supplierId && o.DocumentIdentity.CompanyId == companyId && o.TenantId == tenantContext.TenantId)
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new SupplierOrderSummary(
                o.Id,
                o.PONumber,
                o.OrderDate,
                o.Status.ToString(),
                o.TotalAmount))
            .ToListAsync(cancellationToken);

        var submissions = await db.SupplierCollaborationEntries
            .Where(s => s.SupplierId == supplierId && s.CompanyId == companyId && s.TenantId == tenantContext.TenantId)
            .OrderByDescending(s => s.Id)
            .Select(s => new SupplierCollaborationSummary(
                s.Id,
                s.PurchaseOrderId,
                s.EntryType.ToString(),
                s.SupplierReference,
                s.ProposedDeliveryDate,
                s.InvoiceReferenceNumber,
                s.InvoiceAmount,
                s.Status.ToString(),
                s.BuyerReviewNotes))
            .ToListAsync(cancellationToken);

        return new SupplierPortalOverview(
            supplierId,
            supplier.Name,
            orders,
            submissions);
    }

    public async Task<SupplierCollaborationSummary> SubmitSupplierAcknowledgmentAsync(
        SubmitSupplierAcknowledgmentRequest request,
        string contactEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedEmail = contactEmail.Trim().ToLowerInvariant();

        var contact = await db.PortalContacts.FirstOrDefaultAsync(
            c => c.TenantId == tenantContext.TenantId &&
                 c.PortalType == PortalType.Supplier &&
                 c.PartyId == request.SupplierId &&
                 c.Email.ToLower() == normalizedEmail &&
                 c.IsActive,
            cancellationToken);

        if (contact is null)
            throw new UnauthorizedAccessException("Supplier contact is not authorized.");

        var po = await db.PurchaseOrders
            .Include(p => p.DocumentIdentity)
            .FirstOrDefaultAsync(
                p => p.Id == request.PurchaseOrderId && p.SupplierId == request.SupplierId && p.TenantId == tenantContext.TenantId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Purchase order not found for this supplier.");

        var entry = new SupplierCollaborationEntry
        {
            TenantId = tenantContext.TenantId,
            CompanyId = po.DocumentIdentity.CompanyId ?? 0,
            SupplierId = request.SupplierId,
            PurchaseOrderId = request.PurchaseOrderId,
            EntryType = SupplierCollaborationType.Acknowledgment,
            SupplierReference = request.SupplierReference,
            ProposedDeliveryDate = request.ProposedDeliveryDate,
            Status = CollaborationStatus.PendingBuyerReview
        };

        db.SupplierCollaborationEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return new SupplierCollaborationSummary(
            entry.Id,
            entry.PurchaseOrderId,
            entry.EntryType.ToString(),
            entry.SupplierReference,
            entry.ProposedDeliveryDate,
            null,
            null,
            entry.Status.ToString(),
            null);
    }

    public async Task<SupplierCollaborationSummary> SubmitSupplierInvoiceReferenceAsync(
        SubmitSupplierInvoiceReferenceRequest request,
        string contactEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.InvoiceAmount <= 0)
            throw new ArgumentException("Invoice amount must be positive.");

        var normalizedEmail = contactEmail.Trim().ToLowerInvariant();

        var contact = await db.PortalContacts.FirstOrDefaultAsync(
            c => c.TenantId == tenantContext.TenantId &&
                 c.PortalType == PortalType.Supplier &&
                 c.PartyId == request.SupplierId &&
                 c.Email.ToLower() == normalizedEmail &&
                 c.IsActive,
            cancellationToken);

        if (contact is null)
            throw new UnauthorizedAccessException("Supplier contact is not authorized.");

        var po = await db.PurchaseOrders
            .Include(p => p.DocumentIdentity)
            .FirstOrDefaultAsync(
                p => p.Id == request.PurchaseOrderId && p.SupplierId == request.SupplierId && p.TenantId == tenantContext.TenantId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Purchase order not found for this supplier.");

        var entry = new SupplierCollaborationEntry
        {
            TenantId = tenantContext.TenantId,
            CompanyId = po.DocumentIdentity.CompanyId ?? 0,
            SupplierId = request.SupplierId,
            PurchaseOrderId = request.PurchaseOrderId,
            EntryType = SupplierCollaborationType.InvoiceReferenceSubmission,
            InvoiceReferenceNumber = request.InvoiceReferenceNumber,
            InvoiceAmount = request.InvoiceAmount,
            Status = CollaborationStatus.PendingBuyerReview
        };

        db.SupplierCollaborationEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return new SupplierCollaborationSummary(
            entry.Id,
            entry.PurchaseOrderId,
            entry.EntryType.ToString(),
            null,
            null,
            entry.InvoiceReferenceNumber,
            entry.InvoiceAmount,
            entry.Status.ToString(),
            null);
    }

    public async Task<SupplierCollaborationSummary> ReviewSupplierSubmissionAsync(
        ReviewSupplierSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await db.SupplierCollaborationEntries.FirstOrDefaultAsync(
            e => e.Id == request.SubmissionId && e.TenantId == tenantContext.TenantId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Submission entry not found.");

        entry.Status = request.Approve ? CollaborationStatus.Accepted : CollaborationStatus.Rejected;
        entry.BuyerReviewNotes = request.ReviewNotes;

        await db.SaveChangesAsync(cancellationToken);

        return new SupplierCollaborationSummary(
            entry.Id,
            entry.PurchaseOrderId,
            entry.EntryType.ToString(),
            entry.SupplierReference,
            entry.ProposedDeliveryDate,
            entry.InvoiceReferenceNumber,
            entry.InvoiceAmount,
            entry.Status.ToString(),
            entry.BuyerReviewNotes);
    }
}
