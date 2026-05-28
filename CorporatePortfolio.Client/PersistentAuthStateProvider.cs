using CorporatePortfolio.Services.DTO;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Json;
using System.Security.Claims;

namespace CorporatePortfolio.Client
{
    // Inside your Client WASM Project
    public class PersistentAuthStateProvider : AuthenticationStateProvider
    {
        private readonly HttpClient _httpClient;

        public PersistentAuthStateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                // Fetch the inherited user profile details directly from the server project
                var profile = await _httpClient.GetFromJsonAsync<UserProfileDto>("api/auth/user-profile");

                if (profile is null || !profile.IsAuthenticated)
                {
                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
                }

                // Map inherited string roles straight into local claim structures
                var claims = new List<Claim> { new Claim(ClaimTypes.Name, profile.UserName) };
                claims.AddRange(profile.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

                var identity = new ClaimsIdentity(claims, "ServerAuthentication");
                return new AuthenticationState(new ClaimsPrincipal(identity));
            }
            catch
            {
                // Fail safe to an anonymous user profile if offline
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }
    }

}
