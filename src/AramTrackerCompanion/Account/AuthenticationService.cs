using AramTrackerCompanion.Api;
using AramTrackerCompanion.Settings;

namespace AramTrackerCompanion.Account;

internal enum ServerStatus
{
    Unknown,
    Online,
    Unreachable,
}

internal sealed class AuthenticationService
{
    private readonly AramTrackerApiService _api;
    private readonly SettingsService _settings;
    private readonly TokenStore _tokens;
    private string? _token;

    public AuthenticationService(AramTrackerApiService api, SettingsService settings, TokenStore tokens)
    {
        _api = api;
        _settings = settings;
        _tokens = tokens;
        _token = tokens.Load();
    }

    public event Action? Changed;

    public event Action? AccountsAdded;

    public string? Token => Volatile.Read(ref _token);

    public bool IsLinked => Token is not null;

    public ServerStatus ServerStatus { get; private set; }

    public async Task PairAsync(string code, CancellationToken cancellation)
    {
        var normalized = string.Concat(code.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new ApiException(ApiFailure.InvalidCode, "Enter the pairing code from the website.");
        }

        var response = await ReportAsync(() => _api.PairAsync(normalized, InstallationName(), cancellation));

        _tokens.Save(response.Token);
        Volatile.Write(ref _token, response.Token);
        RememberAccount(response.Installation, response.Players);
        Log.Info("Companion paired", ("installationId", response.Installation.Id));
        Changed?.Invoke();
    }

    public async Task VerifyAsync(CancellationToken cancellation)
    {
        if (Token is not { } token)
        {
            return;
        }

        try
        {
            var me = await ReportAsync(() => _api.GetMeAsync(token, cancellation));
            RememberAccount(me.Installation, me.Players);
        }
        catch (ApiException exception) when (exception.Failure == ApiFailure.Unauthorized)
        {
            Forget("The website no longer recognises this companion");
        }
        catch (ApiException exception)
        {
            Log.Warn("Account check failed", ("reason", exception.Message));
        }

        Changed?.Invoke();
    }

    public async Task<bool> DisconnectAsync(CancellationToken cancellation)
    {
        var revoked = true;

        if (Token is { } token)
        {
            try
            {
                await ReportAsync(() => RevokeAsync(token, cancellation));
            }
            catch (ApiException exception) when (exception.Failure != ApiFailure.Unauthorized)
            {
                revoked = false;
                Log.Warn("Token could not be revoked on the website", ("reason", exception.Message));
            }
            catch (ApiException)
            {
            }
        }

        Forget("Disconnected by the user");
        return revoked;
    }

    public void ReportUnauthorized() => Forget("The website refused the token");

    public void ReportServerStatus(ServerStatus status)
    {
        if (ServerStatus != status)
        {
            ServerStatus = status;
            Changed?.Invoke();
        }
    }

    private async Task<bool> RevokeAsync(string token, CancellationToken cancellation)
    {
        await _api.RevokeAsync(token, cancellation);
        return true;
    }

    private async Task<T> ReportAsync<T>(Func<Task<T>> call)
    {
        try
        {
            var result = await call();
            ServerStatus = ServerStatus.Online;
            return result;
        }
        catch (ApiException exception)
        {
            ServerStatus = exception.Failure is ApiFailure.Network or ApiFailure.Server ? ServerStatus.Unreachable : ServerStatus.Online;
            throw;
        }
    }

    private void RememberAccount(InstallationInfo installation, List<LinkedPlayer>? players)
    {
        var accounts = (players ?? []).Select(p => p.RiotId).Where(id => id.Length > 0).ToList();
        var known = _settings.Read(s => s.LinkedAccounts.ToHashSet(StringComparer.OrdinalIgnoreCase));

        _settings.Update(settings =>
        {
            settings.InstallationId = installation.Id;
            settings.InstallationName = installation.Name;
            settings.LinkedAccounts = accounts;
        });

        if (accounts.Any(account => !known.Contains(account)))
        {
            AccountsAdded?.Invoke();
        }
    }

    private void Forget(string reason)
    {
        if (Interlocked.Exchange(ref _token, null) is null)
        {
            return;
        }

        _tokens.Clear();
        _settings.Update(settings =>
        {
            settings.InstallationId = null;
            settings.InstallationName = null;
            settings.LinkedAccounts = [];
        });

        Log.Info("Token forgotten", ("reason", reason));
        Changed?.Invoke();
    }

    private static string InstallationName()
    {
        var name = Environment.MachineName.Trim();
        return name.Length switch
        {
            0 => "Windows PC",
            > 60 => name[..60],
            _ => name,
        };
    }
}
