using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;

namespace EnterpriseInventory.Api.Security;

/// <summary>Where the keys that encrypt the session and CSRF cookies are kept, from the <c>DataProtection</c> section.</summary>
internal sealed class DataProtectionSettings
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Folder for the key ring. Required outside Development, so a restart or an IIS application pool recycle
    /// does not sign everyone out. Access must be limited to the application pool identity and administrators.
    /// </summary>
    public string? KeysDirectory { get; set; }
}

internal static class DataProtectionSetup
{
    /// <summary>
    /// Data Protection for the cookies, with the key ring in <see cref="DataProtectionSettings.KeysDirectory"/>. On
    /// Windows the keys are encrypted at rest with DPAPI.
    /// </summary>
    public static IServiceCollection AddApiDataProtection(this IServiceCollection services)
    {
        services.AddOptions<DataProtectionSettings>()
            .BindConfiguration(DataProtectionSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DataProtectionSettings>, DataProtectionSettingsValidator>();

        services.AddDataProtection().SetApplicationName("EnterpriseInventory");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<DataProtectionSettings>, ILoggerFactory>((keys, settings, loggerFactory) =>
            {
                // Empty only in Development (see the validator), where the default per-user key folder is used.
                if (string.IsNullOrWhiteSpace(settings.Value.KeysDirectory))
                {
                    return;
                }

                keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(settings.Value.KeysDirectory), loggerFactory);
                if (OperatingSystem.IsWindows())
                {
                    keys.XmlEncryptor = new DpapiXmlEncryptor(protectToLocalMachine: true, loggerFactory);
                }
            });

        return services;
    }

    private sealed class DataProtectionSettingsValidator(IHostEnvironment environment) : IValidateOptions<DataProtectionSettings>
    {
        public ValidateOptionsResult Validate(string? name, DataProtectionSettings options)
        {
            var directory = options.KeysDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                return environment.IsDevelopment()
                    ? ValidateOptionsResult.Success
                    : ValidateOptionsResult.Fail(
                        "DataProtection:KeysDirectory is required outside Development; without it the keys that protect the session cookies are not kept safely across restarts.");
            }

            return directory.Contains("CHANGE-ME", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(directory)
                ? ValidateOptionsResult.Fail($"DataProtection:KeysDirectory must be the full path of the keys folder, not '{directory}'.")
                : ValidateOptionsResult.Success;
        }
    }
}
