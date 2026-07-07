using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ProtoFast.ServiceDefaults.InternalAuth;

public static class InternalJwtAuthExtensions
{
    public static IServiceCollection AddInternalJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<InternalJwtValidationOptions>(configuration.GetSection("InternalJwt"));
        services.AddSingleton<InternalJwtValidator>();
        services.AddSingleton<InternalJwtAuthInterceptor>();
        return services;
    }
}
