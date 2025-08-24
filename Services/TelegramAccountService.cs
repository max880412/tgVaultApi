using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using TL;
using WTelegram;

public interface ITelegramAccountService
{
    Task<LoginStartResponse> StartLoginAsync(string phone, string? password);
    Task<TelegramAccountInfo> CompleteLoginAsync(Guid loginId, string code);
    IEnumerable<TelegramAccountInfo> GetAccounts();
}

internal sealed class LoginSession
{
    public Guid LoginId { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string? Password { get; set; }
    public string? Code { get; set; }
    public Client Client { get; set; } = default!;
}

public class TelegramAccountService : ITelegramAccountService
{
    private readonly TelegramOptions _options;
    private readonly ITelegramUpdatesPublisher _publisher;

    private readonly ConcurrentDictionary<string, TelegramAccountInfo> _accounts = new();
    private readonly ConcurrentDictionary<string, Client> _activeClients = new(); // key: userId
    private readonly ConcurrentDictionary<Guid, LoginSession> _pending = new();

    public TelegramAccountService(IOptions<TelegramOptions> options, ITelegramUpdatesPublisher publisher)
    {
        _options = options.Value;
        _publisher = publisher;
        Directory.CreateDirectory(_options.SessionDir);
    }

    private string? Config(LoginSession session, string what) => what switch
    {
        "api_id" => _options.ApiId.ToString(),
        "api_hash" => _options.ApiHash,
        "phone_number" => session.Phone,
        "verification_code" => session.Code,
        "password" => session.Password,
        "session_pathname" => Path.Combine(_options.SessionDir, new string(session.Phone.Where(char.IsLetterOrDigit).ToArray()) + ".session"),
        _ => null
    };

    public async Task<LoginStartResponse> StartLoginAsync(string phone, string? password)
    {
        var session = new LoginSession
        {
            LoginId = Guid.NewGuid(),
            Phone = phone,
            Password = password
        };

        var client = new Client(what => Config(session, what));
        session.Client = client;
        client.OnUpdate += updates => HandleUpdateAsync(client, updates);

        var user = await client.LoginUserIfNeeded();
        if (user is TL.User u)
        {
            await OnLoggedInAsync(client, u);
            return new LoginStartResponse(session.LoginId, "logged_in");
        }

        _pending[session.LoginId] = session;
        return new LoginStartResponse(session.LoginId, "code_required");
    }

    public async Task<TelegramAccountInfo> CompleteLoginAsync(Guid loginId, string code)
    {
        if (!_pending.TryGetValue(loginId, out var session))
            throw new InvalidOperationException("loginId inválido o expirado");

        session.Code = code;
        var user = await session.Client.LoginUserIfNeeded();
        if (user is not TL.User u)
            throw new InvalidOperationException("No se pudo completar el login");

        await OnLoggedInAsync(session.Client, u);
        _pending.TryRemove(loginId, out _);
        return Map(u);
    }

    public IEnumerable<TelegramAccountInfo> GetAccounts() => _accounts.Values;

    private async Task HandleUpdateAsync(Client client, UpdatesBase updates)
    {
        try
        {
            const long servicePeerId = 777000;
            foreach (var update in updates.UpdateList)
            {
                if (update is UpdateNewMessage { message: Message msg })
                {
                    if (msg.peer_id is PeerUser pu && pu.user_id == servicePeerId)
                    {
                        if (!string.IsNullOrWhiteSpace(msg.message))
                        {
                            var code = ExtractDigits(msg.message) ?? msg.message;
                            string account = client.User?.phone ?? "unknown";
                            await _publisher.PublishLoginCodeAsync(account, code, DateTimeOffset.UtcNow);
                        }
                    }
                }
            }
        }
        catch
        {
            // ignore parsing issues
        }
    }

    private Task OnLoggedInAsync(Client client, TL.User user)
    {
        var info = Map(user);
        _accounts[info.UserId] = info;
        _activeClients[info.UserId] = client; // Keep client alive for updates
        return Task.CompletedTask;
    }

    private static TelegramAccountInfo Map(TL.User user) => new(
        user.id.ToString(), user.phone, user.username, user.first_name, user.last_name);

    private static string? ExtractDigits(string input)
    {
        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (digits.Length >= 5 && digits.Length <= 8) return digits;
        return null;
    }
}
