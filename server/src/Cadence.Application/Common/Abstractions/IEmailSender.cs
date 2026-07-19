namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// Outbound email — invitations, digests, notification emails the user opted into.
/// </summary>
/// <remarks>
/// Always called from a background job, never on the request path: a slow mail provider must not
/// turn "invite a teammate" into a ten-second request, and a bounced send must be retryable
/// without the user replaying the action (§14.2).
/// </remarks>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string PlainTextBody);
