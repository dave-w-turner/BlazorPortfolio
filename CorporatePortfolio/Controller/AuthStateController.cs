using CorporatePortfolio.Services.DTO;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CorporatePortfolio.Controller
{
    // Inside your Backend Server Project Controller
    [ApiController]
    [Route("api/auth")]
    public class AuthStateController : ControllerBase
    {
        [HttpGet("user-profile")]
        public IActionResult GetUserProfileState()
        {
            if (User.Identity is null || !User.Identity.IsAuthenticated)
            {
                return AnonymousState();
            }

            // Gather all roles assigned to this user from the server session
            var roles = User.Claims
                .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                .Select(c => c.Value)
                .ToList();

            return Ok(new UserProfileDto
            {
                IsAuthenticated = true,
                UserName = User.Identity.Name ?? "User",
                Roles = roles
            });
        }

        private IActionResult AnonymousState() => Ok(new UserProfileDto { IsAuthenticated = false });
    }
}
