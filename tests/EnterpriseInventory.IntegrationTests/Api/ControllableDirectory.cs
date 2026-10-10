using EnterpriseInventory.Application.Authentication;

namespace EnterpriseInventory.IntegrationTests.Api;

/// <summary>A directory with one member, whose access each test decides.</summary>
internal sealed class ControllableDirectory : IDirectoryService
{
    public const string Password = "controllable-password";

    // A user of its own for every test, so tests sharing the database never see each other's sessions.
    public Guid ObjectGuid { get; } = Guid.NewGuid();

    public string UserName => "s" + ObjectGuid.ToString("N")[..12];

    public DirectoryAccessStatus Access { get; set; } = DirectoryAccessStatus.Allowed;

    public int AccessChecks { get; private set; }

    public Task<DirectorySignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken) =>
        Task.FromResult(userName == UserName && password == Password
            ? DirectorySignInResult.Succeeded(new DirectoryAccount(ObjectGuid, "S-1-5-21-1-2-3-1201", UserName, "Oturum Testi"))
            : DirectorySignInResult.Failed(DirectorySignInStatus.InvalidCredentials));

    public Task<DirectoryAccessStatus> CheckAccessAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        AccessChecks++;
        return Task.FromResult(objectGuid == ObjectGuid ? Access : DirectoryAccessStatus.AccountNotFound);
    }
}
