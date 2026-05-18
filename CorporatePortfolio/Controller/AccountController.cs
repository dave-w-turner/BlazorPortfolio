using CorporatePortfolio.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CorporatePortfolio.Controller;

[ApiController]
[Route("[controller]")]
public class AccountController(SignInManager<ApplicationUser> signInManager) : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;

    [HttpPost("PerformExternalLogin")]
    public IActionResult PerformExternalLogin([FromForm] string provider, [FromForm] string returnUrl = "/")
    {
        var redirectUrl = Url.Action("ExternalLoginCallback", "Account", new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);

        return Challenge(properties, provider);
    }

    [HttpGet("ExternalLoginCallback")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = "/", string? remoteError = null)
    {
        returnUrl ??= "/";

        if (remoteError != null)
        {
            return Redirect($"/login?error={Uri.EscapeDataString(remoteError)}");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return Redirect("/login");
        }

        var user = await _signInManager.UserManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);

        if (user != null)
        {
            await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
            return LocalRedirect(returnUrl);
        }

        // Auto-register the user if they don't exist yet
        var newEmail = info.Principal.FindFirstValue(ClaimTypes.Email) ?? info.Principal.FindFirst("preferred_username")?.Value;
        if (newEmail != null)
        {
            var newUser = new ApplicationUser { UserName = newEmail, Email = newEmail, EmailConfirmed = true };
            var createResult = await _signInManager.UserManager.CreateAsync(newUser);
            if (createResult.Succeeded)
            {
                createResult = await _signInManager.UserManager.AddLoginAsync(newUser, info);
                if (createResult.Succeeded)
                {
                    await _signInManager.SignInAsync(newUser, isPersistent: false, info.LoginProvider);
                    return LocalRedirect(returnUrl);
                }
            }
        }

        return Redirect("/login");
    }

    [HttpPost("Logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return LocalRedirect("/");
    }
}
