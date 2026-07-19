using Cadence.Application.Common.Models;
using Shouldly;

namespace Cadence.UnitTests.Application;

public class PagingTests
{
    private sealed record MeetingListQuery : ListQuery;

    [Fact]
    public void TotalPages_RoundsUp_SoAPartialLastPageIsNotLost()
    {
        new PagedResult<string>([], total: 41, page: 1, pageSize: 20).TotalPages.ShouldBe(3);
    }

    [Fact]
    public void TotalPages_IsZeroForAnEmptyResult()
    {
        PagedResult<string>.Empty(page: 1, pageSize: 20).TotalPages.ShouldBe(0);
    }

    [Fact]
    public void TotalPages_DoesNotDivideByZero()
    {
        new PagedResult<string>([], total: 10, page: 1, pageSize: 0).TotalPages.ShouldBe(0);
    }

    [Fact]
    public void PageSize_IsClamped_SoNoCallerCanRequestTheWholeTable()
    {
        new MeetingListQuery { PageSize = 5_000 }.PageSize.ShouldBe(ListQuery.MaxPageSize);
    }

    [Fact]
    public void ANonsensePageSize_FallsBackToTheDefault()
    {
        new MeetingListQuery { PageSize = 0 }.PageSize.ShouldBe(ListQuery.DefaultPageSize);
        new MeetingListQuery { PageSize = -3 }.PageSize.ShouldBe(ListQuery.DefaultPageSize);
    }

    [Fact]
    public void ANonsensePageNumber_IsClampedRatherThanRejected()
    {
        // A bad page number is not worth a 400; clamping keeps the response shape predictable.
        new MeetingListQuery { Page = 0 }.Page.ShouldBe(1);
        new MeetingListQuery { Page = -7 }.Page.ShouldBe(1);
    }

    [Fact]
    public void Skip_IsDerivedFromTheClampedValues()
    {
        new MeetingListQuery { Page = 3, PageSize = 20 }.Skip.ShouldBe(40);
        new MeetingListQuery { Page = 1, PageSize = 20 }.Skip.ShouldBe(0);
    }

    [Fact]
    public void SortingDefaultsToNewestFirst()
    {
        // Qualified: Shouldly also ships a SortDirection, and both namespaces are in scope here.
        new MeetingListQuery().SortDir.ShouldBe(Cadence.Application.Common.Models.SortDirection.Desc);
    }
}
