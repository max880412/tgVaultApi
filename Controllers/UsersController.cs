using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace tgVaultApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;
    private readonly IConfiguration _cfg;

    public UsersController(IUserService users, IConfiguration cfg)
    { _users = users; _cfg = cfg; }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var username = await _users.AuthenticateAsync(req.Username, req.Password);
        if (username is null) return Unauthorized();

        var jwt = _cfg.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"] ?? "dev_super_secret_key_change_me"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(int.TryParse(jwt["ExpireMinutes"], out var m) ? m : 60);

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: new[] { new Claim(ClaimTypes.Name, username) },
            expires: expires,
            signingCredentials: creds
        );
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return Ok(new { access_token = tokenString, token_type = "Bearer", expires_in = (int)(expires - DateTime.UtcNow).TotalSeconds });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        var adminUser = _cfg.GetValue<string>("Admin:Username") ?? "admin";
        if (!string.Equals(User.Identity?.Name, adminUser, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var ok = await _users.CreateUserAsync(req.Username, req.Password);
        if (!ok) return Conflict("username ya existe");
        return Ok();
    }
}
