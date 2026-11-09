namespace RipVerify.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using RipVerify.Core.Abstractions;
using RipVerify.Infrastructure.Audio;
using RipVerify.Infrastructure.Batch;
using RipVerify.Infrastructure.Calibration;
using RipVerify.Infrastructure.Cue;
using RipVerify.Infrastructure.FileSystem;
using RipVerify.Infrastructure.Logs;
using RipVerify.Infrastructure.MusicBrainz;
using RipVerify.Infrastructure.Process;
using RipVerify.Infrastructure.Reporting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRipVerifyInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IFolderScanner, FolderScanner>();
        services.AddSingleton<ICueFixerService, CueFixerService>();
        services.AddSingleton<ICueSanitizer, CueSanitizer>();
        services.AddSingleton<ICueGenerator, CueGenerator>();
        services.AddSingleton<ICueMetadataReader, TagLibMetadataReader>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ICueToolsRunner, CueToolsRunner>();
        services.AddSingleton<CueToolsOutputParser>();
        services.AddSingleton<FfmpegPcmReader>();
        services.AddSingleton<AudioCrc32Reader>();
        services.AddSingleton<IRipLogVerifier, RipLogVerifier>();
        services.AddSingleton<ISpectrumAnalyzer, FftSpectrumAnalyzer>();
        services.AddSingleton<IOriginSignalReader, OriginSignalReader>();
        services.AddSingleton<ICalibrationCorpus, JsonCalibrationCorpus>();
        services.AddSingleton<IMusicBrainzClient, MusicBrainzClient>();
        services.AddSingleton<MusicBrainzRateLimiter>();
        services.AddSingleton<MusicBrainzCache>();
        services.AddSingleton<BrickwallCalibrator>();
        services.AddSingleton<IBatchProcessor, BatchProcessor>();
        services.AddSingleton<IReportDocumentBuilder, ReportDocumentBuilder>();
        services.AddSingleton<MarkdownReportRenderer>();
        services.AddSingleton<IReportWriter, ReportWriter>();
        return services;
    }
}
