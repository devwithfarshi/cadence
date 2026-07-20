using System.Text;
using Cadence.Api.Realtime;
using Cadence.Application.Common.Abstractions;
using Cadence.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cadence.Api.Configuration;

/// <summary>
/// JWT bearer validation and the role policies endpoints declare.
/// </summary>
internal static class AuthenticationConfiguration
{
    public const string RequireMember = "RequireMember";
    public const string RequireAdmin = "RequireAdmin";
    public const string RequireOwner = "RequireOwner";

    public static IServiceCollection AddCadenceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                "Jwt configuration is missing. Copy .env.example to .env and set Jwt__SigningKey.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Every one of these is on by default, but stated explicitly: a validation that
                    // is silently disabled by a future default change is a silent authentication
                    // bypass, and this is the one place where being explicit is worth the noise.
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,

                    // Default is five minutes, which would keep a 15-minute token working for
                    // twenty. Servers run NTP; the allowance is not needed.
                    ClockSkew = TimeSpan.Zero,

                    // Keep the claim names as issued. Without this, ASP.NET rewrites `sub` into a
                    // long WS-Federation URI and CurrentUser stops finding it.
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = CadenceClaims.Role,
                };

                options.MapInboundClaims = false;

                // A websocket cannot carry an Authorization header, so SignalR sends the token in
                // the query string. Accepted for hub paths only — see HubAuthentication.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        // Let the exception handler produce problem+json rather than the default
                        // empty 401 body, so every failure the client meets has one shape.
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return context.Response.WriteAsJsonAsync(new
                        {
                            type = "https://cadence.app/errors/unauthorized",
                            title = "Authentication is required.",
                            status = StatusCodes.Status401Unauthorized,
                            detail = "Sign in, or refresh your access token.",
                            instance = context.Request.Path.Value,
                            correlationId = context.HttpContext.TraceIdentifier,
                        });
                    },
                }.WithHubQueryStringToken();
            });

        services.AddAuthorizationBuilder()
            // Coarse capability only. "May this user edit *this* action item?" cannot be expressed
            // in a claim and is checked in the handler against the loaded aggregate (§4.5).
            .AddPolicy(RequireMember, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(CadenceClaims.Role, "owner", "admin", "member"))
            .AddPolicy(RequireAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(CadenceClaims.Role, "owner", "admin"))
            .AddPolicy(RequireOwner, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(CadenceClaims.Role, "owner"));

        return services;
    }
}
