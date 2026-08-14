using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.Interfaces;

namespace ledgerflowApi.API.BackgroundServices;

/// <summary>
/// Periodically scans for invoices whose due date has passed and marks them Overdue.
///
/// This is pure scheduling/orchestration — it does not decide whether a transition is
/// valid. That rule lives entirely in <see cref="Invoice.MarkAsOverdue"/> and
/// <c>InvoiceStatus.CanTransitionTo</c>, which this service calls into rather than
/// duplicating. The domain method only allows Issued/PartiallyPaid → Overdue, so this
/// job can never touch a Draft, Paid, or Voided invoice, even if the query below were
/// ever loosened.
///
/// Design notes:
/// - Runs across all tenants in one pass. Every write is still scoped to the invoice's
///   own TenantId (read from the loaded aggregate), never inferred or cross-applied —
///   there is no tenant context for a background job to impersonate in the first place.
/// - Safe to run repeatedly / concurrently with itself: each invoice update goes through
///   MarkAsOverdue()'s own guard, so an invoice that's no longer eligible (e.g. paid in the
///   meantime) is skipped with a warning log rather than corrupting state or crashing the run.
/// - Uses IServiceScopeFactory to create a fresh DI scope per run, since the background
///   service itself is a singleton but the repositories/DbContext are scoped.
/// - Every transition is written inside the same IUnitOfWork transaction as its AuditLog
///   entry, matching how every other state change in this codebase is audited.
/// </summary>
public sealed class OverdueInvoiceProcessingService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OverdueInvoiceProcessingService> _logger;
    private readonly TimeSpan _interval;

    public OverdueInvoiceProcessingService(
        IServiceScopeFactory scopeFactory,
        ILogger<OverdueInvoiceProcessingService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var intervalHours = configuration.GetValue<double?>("OverdueProcessing:IntervalHours") ?? 24;
        _interval = TimeSpan.FromHours(intervalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Overdue invoice processing service starting. Run interval: {IntervalHours}h.",
            _interval.TotalHours);

        // Give the app time to finish startup (migrations/seed in dev) before the first run.
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOverdueInvoicesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A failed run must never crash the host — log it and try again next interval.
                _logger.LogError(ex, "Overdue invoice processing run failed unexpectedly.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs a single pass: loads all invoices past their due date, marks each eligible one
    /// Overdue, and writes the matching audit entry. Returns the number of invoices updated.
    /// Public (rather than only reachable via the timer loop) so it can be invoked directly
    /// and deterministically from tests, without waiting on BackgroundService's own schedule.
    /// </summary>
    public async Task<int> ProcessOverdueInvoicesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var invoiceRepository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var auditLogRepository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dateTimeService = scope.ServiceProvider.GetRequiredService<IDateTimeService>();

        var asOf = dateTimeService.UtcNow;
        var candidates = await invoiceRepository.GetOverdueInvoicesAsync(asOf, cancellationToken);

        var markedCount = 0;

        foreach (var invoice in candidates)
        {
            var statusBefore = invoice.Status.Value;

            try
            {
                invoice.MarkAsOverdue();
            }
            catch (DomainException ex)
            {
                // The invoice was eligible when queried but is no longer a valid transition
                // target (e.g. it was paid or voided a moment ago). Skip it — it will simply
                // not be a candidate on the next run. This is what makes the job idempotent.
                _logger.LogWarning(
                    ex,
                    "Skipped invoice {InvoiceId} ('{InvoiceNumber}') during overdue processing: {Message}",
                    invoice.Id, invoice.InvoiceNumber, ex.Message);
                continue;
            }

            await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await invoiceRepository.UpdateAsync(invoice, cancellationToken);

                var audit = AuditLog.ForStatusChange(
                    tenantId: invoice.TenantId,
                    entityType: nameof(Invoice),
                    entityId: invoice.Id,
                    fromStatus: statusBefore,
                    toStatus: invoice.Status.Value,
                    description:
                        $"Invoice '{invoice.InvoiceNumber}' automatically marked Overdue — " +
                        $"due date {invoice.DueDate:yyyy-MM-dd} has passed.",
                    userId: null,
                    userDisplayName: "System (Overdue Processing Job)");

                await auditLogRepository.AddAsync(audit, cancellationToken);
            }, cancellationToken);

            markedCount++;
        }

        if (markedCount > 0)
            _logger.LogInformation("Overdue processing run marked {Count} invoice(s) as Overdue.", markedCount);
        else
            _logger.LogDebug("Overdue processing run found no eligible invoices.");

        return markedCount;
    }
}
