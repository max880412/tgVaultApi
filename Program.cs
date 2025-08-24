using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using tgVaultApi.Data;

var builder = WebApplication.CreateBuilder(args);

// Bind server port from configuration
var configuredPort = builder.Configuration.GetValue<int?>("Server:Port");
if (configuredPort is int port && port > 0)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container.
builder.Services.AddControllers();

// DB
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Default") ?? "Data Source=app.db";
    options.UseSqlite(cs);
});

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "tgVaultApi", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new()
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Ingrese su token JWT en el campo: Bearer {token}",
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "bearer"
    });
    options.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// SignalR
builder.Services.AddSignalR();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true));
});

// JWT Auth
var jwtSection = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSection["Key"] ?? "dev_super_secret_key_change_me");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"].ToString();
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/updates"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// Telegram services
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.AddSingleton<TelegramHub>();
builder.Services.AddSingleton<ITelegramUpdatesPublisher, TelegramUpdatesPublisher>();

builder.Services.AddSingleton<ITelegramAccountService, TelegramAccountService>();

// Simple user service
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

// Migrate DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No HTTPS redirection since we bind to a custom HTTP port
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TelegramHub>("/hubs/updates");

app.Run();

// Options and DI types (kept close for brevity). Move to separate files if needed.
public class TelegramOptions
{
    public int ApiId { get; set; }
    public string ApiHash { get; set; } = string.Empty;
    public string SessionDir { get; set; } = "Sessions";
}

public record LoginStartRequest(string PhoneNumber, string? Password);
public record LoginStartResponse(Guid LoginId, string Status);
public record SubmitCodeRequest(Guid LoginId, string Code);
public record TelegramAccountInfo(string UserId, string? PhoneNumber, string? Username, string? FirstName, string? LastName);

public interface ITelegramUpdatesPublisher
{
    Task PublishLoginCodeAsync(string phoneOrUser, string code, DateTimeOffset receivedAt);
}

public class TelegramUpdatesPublisher : ITelegramUpdatesPublisher
{
    private readonly IHubContext<TelegramHub, ITelegramClient> _hub;
    public TelegramUpdatesPublisher(IHubContext<TelegramHub, ITelegramClient> hub) => _hub = hub;

    public Task PublishLoginCodeAsync(string phoneOrUser, string code, DateTimeOffset receivedAt)
        => _hub.Clients.All.LoginCodeReceived(new LoginCodeNotification(phoneOrUser, code, receivedAt));
}

public interface ITelegramClient
{
    Task LoginCodeReceived(LoginCodeNotification payload);
}

public record LoginCodeNotification(string Account, string Code, DateTimeOffset ReceivedAt);

public class TelegramHub : Hub<ITelegramClient> { }

// Models and services for auth
public record LoginRequest(string Username, string Password);
public record CreateUserRequest(string Username, string Password);

public interface IUserService
{
    Task<string?> AuthenticateAsync(string username, string password);
    Task<bool> CreateUserAsync(string username, string password);
}

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;

    public UserService(AppDbContext db, IConfiguration cfg)
    {
        _db = db; _cfg = cfg;
    }

    public async Task<string?> AuthenticateAsync(string username, string password)
    {
        // Admin hardcoded in appsettings
        var adminUser = _cfg.GetValue<string>("Admin:Username") ?? "admin";
        var adminPass = _cfg.GetValue<string>("Admin:Password") ?? "Admin123!";
        if (username == adminUser && password == adminPass) return "admin";

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Username == username);
        if (user == null) return null;
        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? user.Username : null;
    }

    public async Task<bool> CreateUserAsync(string username, string password)
    {
        if (await _db.Users.AnyAsync(x => x.Username == username)) return false;
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        _db.Users.Add(new AppUser { Username = username, PasswordHash = hash });
        await _db.SaveChangesAsync();
        return true;
    }
}
