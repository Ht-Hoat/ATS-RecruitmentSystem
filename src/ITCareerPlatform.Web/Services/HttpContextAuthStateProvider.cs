using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace ITCareerPlatform.Services;

// Cung cấp trạng thái đăng nhập cho Blazor (server-rendered) từ cookie trong HttpContext.
public class HttpContextAuthStateProvider(IHttpContextAccessor http) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = http.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(user));
    }
}
