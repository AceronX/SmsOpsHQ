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

        // Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITwilioService, TwilioService>();
        services.AddScoped<IPhoneValidationService, PhoneValidationService>();
        services.AddScoped<IStorePhoneResolver, StorePhoneResolver>();
        services.AddScoped<IIdentityResolver, IdentityResolver>();
        services.AddScoped<IQuarantineService, QuarantineService>();
        services.AddScoped<IRealtimeService, RealtimeService>();

        // Singleton: XPD sync service maintains sync state across requests.
        services.AddSingleton<IXpdSyncService, XpdSyncService>();

        // Reminder system (scoped: one service per request, singleton scheduler for background timer).
        services.AddScoped<IReminderService, ReminderService>();
        services.AddSingleton<IReminderScheduler, ReminderScheduler>();

        // Media service for Twilio MMS downloads.
        services.AddHttpClient();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<MediaService>();

        // XPD concurrency limiter (singleton: shared semaphore + stats).
        services.AddSingleton<XpdConcurrencyLimiter>();

        return services;
    }
}
