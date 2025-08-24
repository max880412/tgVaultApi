using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace tgVaultApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TelegramController : ControllerBase
{
    private readonly ITelegramAccountService _accounts;

    public TelegramController(ITelegramAccountService accounts)
    {
        _accounts = accounts;
    }

    [HttpPost("login/start")]
    public async Task<ActionResult<LoginStartResponse>> StartLogin([FromBody] LoginStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber)) return BadRequest("phone requerido");
        var res = await _accounts.StartLoginAsync(request.PhoneNumber, request.Password);
        return Ok(res);
    }

    [HttpPost("login/submit-code")]
    public async Task<ActionResult<TelegramAccountInfo>> SubmitCode([FromBody] SubmitCodeRequest request)
    {
        var info = await _accounts.CompleteLoginAsync(request.LoginId, request.Code);
        return Ok(info);
    }

    [HttpGet("accounts")]
    public ActionResult<IEnumerable<TelegramAccountInfo>> Accounts()
        => Ok(_accounts.GetAccounts());
}
