using Cadence.Domain.Chat;
using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class ConversationTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void AConversationCannotOpenWithAnAssistantTurn()
    {
        // An answer with no question is either a bug or a leaked response from another thread.
        var conversation = Start();

        Should.Throw<DomainException>(() => conversation.Answer("Here is what I found."));
    }

    [Fact]
    public void AskThenAnswer_KeepsTheTurnsInOrder()
    {
        var conversation = Start();

        conversation.Ask("What did we decide about pricing?");
        conversation.Answer("You agreed to hold list price through Q3.");

        conversation.Messages.Select(message => message.Role)
            .ShouldBe([ChatRole.User, ChatRole.Assistant]);
    }

    [Fact]
    public void AppendingAMessage_MovesTheConversationToTheTopOfTheSidebar()
    {
        var conversation = Start();
        var atCreation = conversation.LastMessageAt;

        conversation.Ask("Summarise last week's standups.");

        conversation.LastMessageAt.ShouldBeGreaterThanOrEqualTo(atCreation);
        conversation.LastMessageAt.ShouldBe(conversation.Messages[^1].CreatedAt);
    }

    [Fact]
    public void AnAnswerCarriesItsCitations()
    {
        var conversation = Start();
        var meetingId = Guid.CreateVersion7();
        conversation.Ask("What did we decide about pricing?");

        conversation.Answer(
            "You agreed to hold list price through Q3.",
            [ChatSource.Create(meetingId, ChatSourceKind.Meeting, "Pricing review", $"/meetings/{meetingId}")]);

        conversation.Messages[^1].Sources.ShouldHaveSingleItem().SourceId.ShouldBe(meetingId);
    }

    [Fact]
    public void AUserTurnCarriesNoCitations()
    {
        var conversation = Start();

        conversation.Ask("What did we decide about pricing?");

        conversation.Messages.Single().Sources.ShouldBeEmpty();
    }

    [Fact]
    public void ACitationMustPointAtARealRecord()
    {
        Should.Throw<DomainException>(
            () => ChatSource.Create(Guid.Empty, ChatSourceKind.Document, "Contract", "/documents/x"));
    }

    [Fact]
    public void AnEmptyQuestionIsRejected()
    {
        var conversation = Start();

        Should.Throw<DomainException>(() => conversation.Ask("   "));
    }

    private static Conversation Start() =>
        Conversation.Start(OrganizationId, UserId, "Pricing questions");
}
