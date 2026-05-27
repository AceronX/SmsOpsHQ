using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Repositories;
using SmsOpsHQ.Infrastructure.Services;

namespace SmsOpsHQ.Infrastructure;

// Extension methods to register Infrastructure services into the DI container.
public static class DependencyInjection
{
    // Registers AppDbContext (SQLite), repositories, and services.
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));



        // Bind settings from configuration sections.
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        // In-memory cache (used by IdentityResolver negative cache).
        services.AddMemoryCache();

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IStoreRepository, StoreRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IThreadRepository, ThreadRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<IOptOutRepository, OptOutRepository>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();

        // Services
        services.AddScoped<IAuthService, AuthService>();
        // Register via factory so the service is built from IOptionsSnapshot
        // (per-scope), which lets credential edits in the desktop UI take effect on
        // the next request without restarting the API. See TwilioService.Create.
        services.AddScoped<ITwilioService>(TwilioService.Create);
        services.AddScoped<IPhoneValidationService, PhoneValidationService>();
        services.AddScoped<IStorePhoneResolver, StorePhoneResolver>();
        services.AddScoped<IIdentityResolver, IdentityResolver>();
        services.AddScoped<IQuarantineService, QuarantineService>();
        services.AddScoped<IRealtimeService, RealtimeService>();
        services.AddScoped<IReviewService, ReviewService>();

        // Phase 4 of the central-Twilio-webhook redesign: the inbound + status
        // pipelines were extracted from TwilioInboundController / TwilioStatusController
        // so both the legacy HTTP webhook AND the Hub SignalR receiver (Phase 5)
        // call the same code. Single source of truth for business rules.
        services.AddScoped<IInboundSmsProcessor, InboundSmsProcessor>();
        services.AddScoped<IMessageStatusProcessor, MessageStatusProcessor>();

        // Singleton: persists XPD path/credentials to %AppData%\SmsOpsHQ\xpd_config.json
        // and overlays them on top of appsettings.json. Both manual and scheduled
        // sync read through this service so an operator's saved config takes effect
        // automatically -- no editing files in Program Files, no API restart.
        services.AddSingleton<IXpdConfigService, XpdConfigService>();

        // Singleton: XPD sync service maintains sync state across requests.
        services.AddSingleton<IXpdSyncService, XpdSyncService>();

        // Singleton: hourly (configurable) automatic XPD sync. Idle until Start()
        // is called from Program.cs. Driven by Timer; calls IXpdSyncService.
        services.AddSingleton<IXpdSyncScheduler, XpdSyncScheduler>();

        // Reminder system (scoped: one service per request, singleton scheduler for background timer).
        services.AddScoped<IReminderService, ReminderService>();
        services.AddSingleton<IReminderScheduler, ReminderScheduler>();

        services.AddSingleton<ReviewAutomationSettingsStore>();
        services.AddScoped<IReviewAutomationService, ReviewAutomationService>();
        services.AddSingleton<IReviewAutomationScheduler, ReviewAutomationScheduler>();

        // Media service for Twilio MMS downloads.
        services.AddHttpClient();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<MediaService>();

        // XPD concurrency limiter (singleton: shared semaphore + stats).
        services.AddSingleton<XpdConcurrencyLimiter>();

        return services;
    }
}
