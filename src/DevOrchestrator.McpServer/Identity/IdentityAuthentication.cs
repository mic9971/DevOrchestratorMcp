using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using DevOrchestrator.Domain.Identity;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Identity;

public static class IdentityAuthentication
{
    public const string GitHubScheme = "GitHub";

    public static IServiceCollection AddHumanIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityOptions>()
            .Bind(configuration.GetSection(IdentityOptions.SectionName));
        services.AddScoped<IdentityCookieEvents>();

        var configured = configuration.GetSection(IdentityOptions.SectionName).Get<IdentityOptions>() ?? new IdentityOptions();
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-DevOrchestrator.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.EventsType = typeof(IdentityCookieEvents);
                options.LoginPath = "/auth/login";
                options.AccessDeniedPath = "/auth/denied";
            })
            .AddOAuth(GitHubScheme, options =>
            {
                options.ClientId = string.IsNullOrWhiteSpace(configured.GitHub.ClientId) ? "disabled-client" : configured.GitHub.ClientId;
                options.ClientSecret = string.IsNullOrWhiteSpace(configured.GitHub.ClientSecret) ? "disabled-secret" : configured.GitHub.ClientSecret;
                options.CallbackPath = "/signin-github";
                options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
                options.TokenEndpoint = "https://github.com/login/oauth/access_token";
                options.UserInformationEndpoint = "https://api.github.com/user";
                options.SaveTokens = false;
                options.Scope.Add("read:user");
                options.Scope.Add("user:email");

                options.Events = new OAuthEvents
                {
                    OnCreatingTicket = async context =>
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                        request.Headers.UserAgent.ParseAdd("DevOrchestratorMcp/1.0");
                        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

                        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
                        response.EnsureSuccessStatusCode();
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                        var root = document.RootElement;
                        var subject = root.GetProperty("id").GetInt64().ToString();
                        var login = root.GetProperty("login").GetString() ?? throw new InvalidOperationException("GitHub login is missing.");
                        var displayName = root.TryGetProperty("name", out var name) && name.ValueKind != JsonValueKind.Null ? name.GetString() : login;
                        var email = root.TryGetProperty("email", out var emailElement) && emailElement.ValueKind != JsonValueKind.Null ? emailElement.GetString() : null;
                        var now = DateTime.UtcNow;

                        var db = context.HttpContext.RequestServices.GetRequiredService<OrchestratorDbContext>();
                        var user = await db.HumanIdentityUsers.SingleOrDefaultAsync(
                            x => x.Provider == "github" && x.Subject == subject,
                            context.HttpContext.RequestAborted);
                        if (user is null)
                        {
                            user = HumanIdentityUser.Create("github", subject, login, displayName, email, now);
                            db.HumanIdentityUsers.Add(user);
                        }
                        else
                        {
                            user.RecordLogin(login, displayName, email, now);
                        }

                        var bootstrap = context.HttpContext.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value.BootstrapGitHubLogins;
                        var isBootstrapAdmin = bootstrap.Any(x => string.Equals(x?.Trim(), login, StringComparison.OrdinalIgnoreCase));
                        if (isBootstrapAdmin)
                        {
                            var alreadyAdmin = await db.HumanIdentityRoles.AnyAsync(
                                x => x.UserId == user.Id && x.Role == HumanRoles.Admin,
                                context.HttpContext.RequestAborted);
                            if (!alreadyAdmin)
                                db.HumanIdentityRoles.Add(HumanIdentityRole.Create(user.Id, HumanRoles.Admin, "bootstrap", now));
                        }

                        db.SecurityAuditEvents.Add(SecurityAuditEvent.Create(
                            $"github:{login}", "human", "identity.login", "user", user.Id.ToString(), now,
                            ipAddress: context.HttpContext.Connection.RemoteIpAddress?.ToString()));
                        await db.SaveChangesAsync(context.HttpContext.RequestAborted);

                        var roles = await db.HumanIdentityRoles.AsNoTracking()
                            .Where(x => x.UserId == user.Id)
                            .Select(x => x.Role)
                            .ToListAsync(context.HttpContext.RequestAborted);

                        var identity = (ClaimsIdentity)context.Principal!.Identity!;
                        identity.AddClaim(new Claim(HumanIdentityAccess.UserIdClaim, user.Id.ToString()));
                        identity.AddClaim(new Claim(HumanIdentityAccess.ProviderClaim, "github"));
                        identity.AddClaim(new Claim(HumanIdentityAccess.LoginClaim, login));
                        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
                        identity.AddClaim(new Claim(ClaimTypes.Name, displayName ?? login));
                        foreach (var role in roles) identity.AddClaim(new Claim(ClaimTypes.Role, role));
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }
}

public sealed class IdentityCookieEvents : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var userId = HumanIdentityAccess.UserId(context.Principal!);
        if (!userId.HasValue)
        {
            context.RejectPrincipal();
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<OrchestratorDbContext>();
        var user = await db.HumanIdentityUsers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId.Value, context.HttpContext.RequestAborted);
        if (user is null || !user.IsEnabled)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var roles = await db.HumanIdentityRoles.AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => x.Role)
            .ToListAsync(context.HttpContext.RequestAborted);

        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        foreach (var claim in identity.FindAll(ClaimTypes.Role).ToArray()) identity.RemoveClaim(claim);
        foreach (var role in roles) identity.AddClaim(new Claim(ClaimTypes.Role, role));
    }
}
