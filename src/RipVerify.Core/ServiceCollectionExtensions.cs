namespace RipVerify.Core;

using Microsoft.Extensions.DependencyInjection;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Services;
using RipVerify.Core.Services.Classification;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRipVerifyCore(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFileValidator, FileValidator>();
        services.AddSingleton<Abstractions.IVerificationClassifier, Services.Classification.VerificationClassifier>();
        return services;
    }
}
