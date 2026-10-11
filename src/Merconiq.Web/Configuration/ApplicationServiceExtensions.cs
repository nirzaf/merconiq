using FluentValidation;
using Merconiq.Core.Behaviors;
using Merconiq.Core.Features.Items.Queries;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Options;
using Merconiq.Core.Services;
using Merconiq.Core.Validators;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Repositories;
using Merconiq.Web.BackgroundServices;
using Merconiq.Web.Services;
using MediatR;

namespace Merconiq.Web.Configuration;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddInventoryApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IStockCountService, Merconiq.Infrastructure.Services.StockCountService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<ITransferOrderService, TransferOrderService>();
        services.AddScoped<ITransferAgingReconciliationService,
            Merconiq.Infrastructure.Services.TransferAgingReconciliationService>();
        services.AddScoped<IOrganizationService, Merconiq.Infrastructure.Services.OrganizationService>();
        services.AddScoped<ICompanyMembershipService, Merconiq.Infrastructure.Services.CompanyMembershipService>();
        services.AddScoped<IDocumentNumberService, Merconiq.Infrastructure.Services.DocumentNumberService>();
        services.AddScoped<IDocumentIdentityService, Merconiq.Infrastructure.Services.DocumentIdentityService>();
        services.AddScoped<IMasterDataImportService, Merconiq.Infrastructure.Services.MasterDataImportService>();
        services.AddScoped<IOpeningStockImportService, Merconiq.Infrastructure.Services.OpeningStockImportService>();
        services.AddScoped<IFiscalCalendarService, Merconiq.Infrastructure.Services.FiscalCalendarService>();
        services.AddScoped<IFiscalAccountLockService, Merconiq.Infrastructure.Services.FiscalAccountLockService>();
        services.AddScoped<IPosReceiptService, Merconiq.Infrastructure.Services.PosReceiptService>();
        services.AddScoped<IBankReconciliationService, Merconiq.Infrastructure.Services.BankReconciliationService>();
        services.AddScoped<IFixedAssetService, Merconiq.Infrastructure.Services.FixedAssetService>();
        services.AddScoped<ICustomerCreditService, Merconiq.Infrastructure.Services.CustomerCreditService>();
        services.AddScoped<ICardSettlementService, Merconiq.Infrastructure.Services.CardSettlementService>();
        services.AddScoped<ITillApprovalService, Merconiq.Infrastructure.Services.TillApprovalService>();
        services.AddScoped<IExpenseClaimService, Merconiq.Infrastructure.Services.ExpenseClaimService>();
        services.AddScoped<ICrmService, Merconiq.Infrastructure.Services.CrmService>();
        services.AddScoped<IJournalPostingService, Merconiq.Infrastructure.Services.JournalPostingService>();
        services.AddScoped<IAccountPostingPolicy, Merconiq.Infrastructure.Services.AccountPostingPolicyService>();
        services.AddScoped<IGoodsReceiptService, Merconiq.Infrastructure.Services.GoodsReceiptService>();
        services.AddScoped<IPurchaseInvoiceService, Merconiq.Infrastructure.Services.PurchaseInvoiceService>();
        services.AddScoped<IPaymentService, Merconiq.Infrastructure.Services.PaymentService>();
        services.AddScoped<ISalesService, Merconiq.Infrastructure.Services.SalesService>();
        services.AddScoped<IPosService, Merconiq.Infrastructure.Services.PosService>();
        services.AddScoped<ICreditNoteService, Merconiq.Infrastructure.Services.CreditNoteService>();
        services.AddScoped<IFinanceStatementService, Merconiq.Infrastructure.Services.FinanceStatementService>();
        services.AddScoped<IProjectService, Merconiq.Infrastructure.Services.ProjectService>();
        services.AddScoped<IHumanResourcesService, Merconiq.Infrastructure.Services.HumanResourcesService>();
        services.AddScoped<IPayrollService, Merconiq.Infrastructure.Services.PayrollService>();
        services.AddScoped<IManufacturingService, Merconiq.Infrastructure.Services.ManufacturingService>();

        services.AddOptions<ForecastingOptions>()
            .Bind(configuration.GetSection(ForecastingOptions.SectionName))
            .Validate(options => ForecastingImplementations.IsSupported(options.Implementation),
                $"{ForecastingOptions.SectionName}:Implementation must be '{ForecastingImplementations.ManagedMovingAverage}' or '{ForecastingImplementations.Ssa}'.")
            .Validate(ForecastingOptions.HasValidResourceLimits,
                $"{ForecastingOptions.SectionName}:MaxForecastHorizonDays must be between 1 and {ForecastingOptions.AbsoluteMaxForecastHorizonDays}; MaxHistoricalDays must be between {ForecastingOptions.MinimumHistoricalDays} and {ForecastingOptions.AbsoluteMaxHistoricalDays}.")
            .ValidateOnStart();
        services.AddScoped<IDemandForecastService, DemandForecastService>();
        services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();

        services.AddHttpClient("Webhooks", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<IWebhookDispatcher, Merconiq.Infrastructure.Services.WebhookDispatcher>();
        services.AddScoped<IIdempotencyKeyStore, IdempotencyKeyStore>();

        services.AddValidatorsFromAssemblyContaining<ItemValidator>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetAllItemsQuery).Assembly));

        services.AddSingleton<ITenantForecastRunner, TenantForecastRunner>();
        services.AddHostedService<ForecastBackgroundService>();
        services.AddHostedService<Merconiq.Web.BackgroundServices.WebhookDeliveryBackgroundService>();

        return services;
    }
}
