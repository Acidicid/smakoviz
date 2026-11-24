using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reviews.Application.Behaviors;

namespace Reviews.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Реєстрація шару Application: MediatR (сканування всіх обробників збірки) + pipeline behaviors
    /// + валідатори FluentValidation. Порядок AddOpenBehavior = порядок виконання.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.LicenseKey = configuration["LuckyPenny:LicenseKey"];
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));      // 1 — зовнішній
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));   // 2 — відсікає некоректні запити
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));  // 3 — найближче до обробника
        });

        // Scoped: асинхронні валідатори використовують scoped-репозиторії (запити до MongoDB)
        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped);

        return services;
    }
}
