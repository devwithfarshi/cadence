using Cadence.Api.Common;
using Microsoft.AspNetCore.SignalR;

namespace Cadence.Api.Realtime;

/// <summary>
/// Stages the caller's identity where <see cref="CurrentUser"/> can find it, for every hub call.
/// </summary>
/// <remarks>
/// <para>
/// A filter rather than a line at the top of each hub method. Forgetting that line does not break
/// the build or throw — the tenant filter simply matches nothing and the method behaves as though
/// the meeting does not exist. A guard whose omission is invisible has to be applied centrally, for
/// the same reason the tenant filter itself is (§3.3).
/// </para>
/// <para>
/// <c>HubInvocationContext.ServiceProvider</c> is the invocation's own scope — the one the hub and
/// its <c>ICurrentUser</c> are resolved from — so this writes to the same instance they will read.
/// </para>
/// </remarks>
public sealed class HubPrincipalFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        invocationContext.ServiceProvider
            .GetRequiredService<ScopedPrincipal>()
            .Principal = invocationContext.Context.User;

        return await next(invocationContext);
    }
}
