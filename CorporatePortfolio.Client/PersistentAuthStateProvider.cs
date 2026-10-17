using CorporatePortfolio.Services.DTO;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Json;
using System.Security.Claims;

namespace CorporatePortfolio.Client
{
    public class PersistentAuthStateProvider(HttpClient httpClient) : AuthenticationStateProvider
    {
        private readonly HttpClient _httpClient = httpClient;

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var profile = await _httpClient.GetFromJsonAsync<UserProfileDto>("api/auth/user-profile");

                if (profile is null || !profile.IsAuthenticated)
                {
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
                }

                var claims = new List<Claim> { new Claim(ClaimTypes.Name, profile.UserName) };
                claims.AddRange(profile.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

                var identity = new ClaimsIdentity(claims, "ServerAuthentication");
                return new AuthenticationState(new ClaimsPrincipal(identity));
            }
            catch
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }
    }

}
