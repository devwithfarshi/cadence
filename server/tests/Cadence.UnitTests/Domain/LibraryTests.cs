using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Library;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class LibraryTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid OwnerId = Guid.CreateVersion7();

    [Fact]
    public void ANewDocument_StartsProcessing_NotIndexed()
    {
        // The row is written after the upload succeeds but before indexing runs, so the UI can show
        // an honest in-progress state rather than an empty search result.
        var document = RegisterDocument();

        document.ProcessingStatus.ShouldBe(ProcessingStatus.Processing);
    }

    [Fact]
    public void AFailedIndexing_IsSurfaced_NotSwallowed()
    {
        var document = RegisterDocument();

        document.MarkFailed();

        document.ProcessingStatus.ShouldBe(ProcessingStatus.Failed);
    }

    [Fact]
    public void ADocumentWithoutAStorageKey_IsRejected()
    {
        // Without the Cloudinary publicId the asset can never be deleted or re-signed.
        Should.Throw<DomainException>(() => RegisterDocument(storageKey: " "));
    }

    [Fact]
    public void Tags_AreNormalisedAndDeduplicated()
    {
        var document = RegisterDocument();

        document.ReplaceTags(["  Pricing ", "PRICING", "q3", ""]);

        document.Tags.ShouldBe(["pricing", "q3"]);
    }

    [Fact]
    public void ALinkEntry_MustCarryItsUrl()
    {
        var act = () => KnowledgeItem.Create(
            OrganizationId,
            OwnerId,
            "Competitor pricing teardown",
            KnowledgeItemKind.Link,
            "Research",
            "External analysis of list pricing.");

        Should.Throw<DomainException>(act);
    }

    [Fact]
    public void AnUncategorisedEntry_GetsAFallbackCategory_RatherThanABlankFilterValue()
    {
        var item = KnowledgeItem.Create(
            OrganizationId,
            OwnerId,
            "Onboarding checklist",
            KnowledgeItemKind.MeetingNote,
            category: "  ",
            "Steps for a new hire's first week.");

        item.Category.ShouldBe("Uncategorised");
    }

    [Fact]
    public void MarkOpened_DrivesTheRecentlyOpenedRail()
    {
        var item = KnowledgeItem.Create(
            OrganizationId,
            OwnerId,
            "Onboarding checklist",
            KnowledgeItemKind.MeetingNote,
            "People",
            "Steps for a new hire's first week.");

        item.LastOpenedAt.ShouldBeNull();
        item.MarkOpened();
        item.LastOpenedAt.ShouldNotBeNull();
    }

    private static Document RegisterDocument(string storageKey = "cadence/docs/q3-forecast") =>
        Document.Register(
            OrganizationId,
            OwnerId,
            "Q3 forecast.pdf",
            DocumentType.Pdf,
            sizeBytes: 248_112,
            storageKey,
            url: "https://res.cloudinary.com/cadence/raw/upload/q3-forecast.pdf");
}
