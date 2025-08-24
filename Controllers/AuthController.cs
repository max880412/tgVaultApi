using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace tgVaultApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    // Deprecated: Use /api/users/login instead.
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Info() => Ok(new { message = "Use /api/users/login" });
}
