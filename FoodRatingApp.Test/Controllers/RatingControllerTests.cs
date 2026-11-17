using FoodRatingApp.Controllers;
using FoodRatingApp.Model;
using FoodRatingApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace FoodRatingApp.Test.Controllers;

public class RatingControllerTests
{
    /// <summary>
    /// A fake FSA client holding <paramref name="count"/> authorities that, like the real FSA API,
    /// returns only the page that was asked for.
    /// </summary>
    private static IFsaClient FsaWithAuthorities(int count)
    {
        var all = Enumerable.Range(1, count)
            .Select(i => new FsaAuthority { LocalAuthorityId = i, Name = $"Authority {i:D3}" })
            .ToList();
        var fsaClient = Substitute.For<IFsaClient>();
        fsaClient.GetAuthoritiesPage(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            int pageNumber = call.ArgAt<int>(0), pageSize = call.ArgAt<int>(1);
            var skip = (long)(pageNumber - 1) * pageSize;
            return new FsaAuthorityList { Authorities = skip >= all.Count ? [] : all.Skip((int)skip).Take(pageSize).ToList() };
        });
        return fsaClient;
    }

    private static PagedResult<Authority> AsPage(IActionResult result)
    {
        var json = result.Should().BeOfType<JsonResult>().Subject;
        return json.Value.Should().BeOfType<PagedResult<Authority>>().Subject;
    }

    [Fact]
    public async Task GetAsync_NoParameters_FetchesOnlyTheFirstPageFromFsa()
    {
        var fsaClient = FsaWithAuthorities(363);

        var page = AsPage(await new RatingController(fsaClient).GetAsync());

        page.Page.Should().Be(1);
        page.PageSize.Should().Be(RatingController.DefaultPageSize);
        page.Items.Should().HaveCount(RatingController.DefaultPageSize);
        await fsaClient.Received(1).GetAuthoritiesPage(1, RatingController.DefaultPageSize);
        fsaClient.ReceivedCalls().Should().HaveCount(1, "only the requested page should be fetched from FSA");
    }

    [Fact]
    public async Task GetAsync_Page3_FetchesOnlyPage3FromFsa()
    {
        var fsaClient = FsaWithAuthorities(50);

        var page = AsPage(await new RatingController(fsaClient).GetAsync(page: 3, pageSize: 10));

        page.Items.Select(a => a.Id).Should().Equal(Enumerable.Range(21, 10));
        page.HasPrevious.Should().BeTrue();
        page.HasNext.Should().BeTrue();
        await fsaClient.Received(1).GetAuthoritiesPage(3, 10);
        fsaClient.ReceivedCalls().Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAsync_MapsFsaFieldsToAuthority()
    {
        var page = AsPage(await new RatingController(FsaWithAuthorities(2)).GetAsync());

        page.Items.Should().BeEquivalentTo([new Authority { Id = 1, Name = "Authority 001" }, new Authority { Id = 2, Name = "Authority 002" }]);
        page.HasPrevious.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_LastPartialPage_HasNoNextPage()
    {
        var page = AsPage(await new RatingController(FsaWithAuthorities(25)).GetAsync(page: 3, pageSize: 10));

        page.Items.Should().HaveCount(5);
        page.HasNext.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_LastPageExactlyFull_NextRequestReturnsEmptyWithNoNext()
    {
        var controller = new RatingController(FsaWithAuthorities(20));

        var full = AsPage(await controller.GetAsync(page: 2, pageSize: 10));
        var after = AsPage(await controller.GetAsync(page: 3, pageSize: 10));

        full.HasNext.Should().BeTrue("a full page cannot tell whether another one follows");
        after.Items.Should().BeEmpty();
        after.HasNext.Should().BeFalse();
    }

    [Theory]
    [InlineData(999)]
    [InlineData(int.MaxValue)]
    public async Task GetAsync_PageBeyondEnd_ReturnsEmptyItems(int pageNumber)
    {
        var page = AsPage(await new RatingController(FsaWithAuthorities(25)).GetAsync(page: pageNumber, pageSize: 10));

        page.Items.Should().BeEmpty();
        page.HasNext.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    public async Task GetAsync_InvalidPaging_ReturnsBadRequestWithoutCallingFsa(int pageNumber, int size)
    {
        var fsaClient = FsaWithAuthorities(5);

        var result = await new RatingController(fsaClient).GetAsync(page: pageNumber, pageSize: size);

        result.Should().BeOfType<BadRequestObjectResult>();
        fsaClient.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_PageSizeAboveMax_IsCappedBeforeCallingFsa()
    {
        var fsaClient = FsaWithAuthorities(500);

        var page = AsPage(await new RatingController(fsaClient).GetAsync(page: 1, pageSize: 1000));

        page.PageSize.Should().Be(RatingController.MaxPageSize);
        page.Items.Should().HaveCount(RatingController.MaxPageSize);
        await fsaClient.Received(1).GetAuthoritiesPage(1, RatingController.MaxPageSize);
    }

    [Fact]
    public async Task GetAsync_NoAuthorities_ReturnsEmptyPage()
    {
        var page = AsPage(await new RatingController(FsaWithAuthorities(0)).GetAsync());

        page.Items.Should().BeEmpty();
        page.HasNext.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_LazyLoadingUntilHasNextIsFalse_ReturnsEachAuthorityOnceWithOneFsaCallPerPage()
    {
        var fsaClient = FsaWithAuthorities(47);
        var controller = new RatingController(fsaClient);
        var seen = new List<int>();

        PagedResult<Authority> page;
        var pageNumber = 1;
        do
        {
            page = AsPage(await controller.GetAsync(page: pageNumber++, pageSize: 10));
            seen.AddRange(page.Items.Select(a => a.Id));
        } while (page.HasNext);

        seen.Should().Equal(Enumerable.Range(1, 47));
        fsaClient.ReceivedCalls().Should().HaveCount(5, "47 authorities at 10 per page is 5 pages, each fetched once");
    }

    [Fact]
    public async Task Get_FhrsAuthority_ReturnsRatingsInFhrsOrderWithPercentages()
    {
        var fsaClient = Substitute.For<IFsaClient>();
        fsaClient.GetAuthority(10).Returns(new FsaAuthority { LocalAuthorityId = 10, Name = "Leeds", SchemeType = (int)RatingScheme.Fhrs });
        fsaClient.GetEstablishments(10).Returns(Establishments("5", "5", "4", "Exempt"));
        var controller = new RatingController(fsaClient);

        var result = await controller.Get(10);

        var ratings = result.Should().BeOfType<JsonResult>().Subject.Value.Should().BeAssignableTo<IEnumerable<AuthorityRatingItem>>().Subject.ToList();
        ratings.Select(r => r.Name).Should().Equal("5", "4", "3", "2", "1", "Exempt");
        ratings.Select(r => r.Value).Should().Equal(50, 25, 0, 0, 0, 25);
    }

    [Fact]
    public async Task Get_FhisAuthority_ReturnsRatingsInFhisOrder()
    {
        var fsaClient = Substitute.For<IFsaClient>();
        fsaClient.GetAuthority(197).Returns(new FsaAuthority { LocalAuthorityId = 197, Name = "Aberdeen City", SchemeType = (int)RatingScheme.Fhis });
        fsaClient.GetEstablishments(197).Returns(Establishments("Pass", "Improvement Required", "Pass and Eat Safe", "Pass"));
        var controller = new RatingController(fsaClient);

        var result = await controller.Get(197);

        var ratings = ((IEnumerable<AuthorityRatingItem>)result.Should().BeOfType<JsonResult>().Subject.Value!).ToList();
        ratings.Select(r => r.Name).Should().Equal("Pass and Eat Safe", "Pass", "Improvement Required");
        ratings.Select(r => r.Value).Should().Equal(25, 50, 25);
    }

    [Fact]
    public async Task Get_UnknownAuthority_ReturnsNotFoundWithoutFetchingEstablishments()
    {
        var fsaClient = Substitute.For<IFsaClient>();
        fsaClient.GetAuthority(Arg.Any<int>()).Returns((FsaAuthority?)null);
        var controller = new RatingController(fsaClient);

        var result = await controller.Get(123456);

        result.Should().BeOfType<NotFoundObjectResult>();
        await fsaClient.DidNotReceive().GetEstablishments(Arg.Any<int>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Get_NonPositiveAuthorityId_ReturnsBadRequest(int authorityId)
    {
        var fsaClient = Substitute.For<IFsaClient>();
        var controller = new RatingController(fsaClient);

        var result = await controller.Get(authorityId);

        result.Should().BeOfType<BadRequestObjectResult>();
        await fsaClient.DidNotReceive().GetAuthority(Arg.Any<int>());
    }

    private static IReadOnlyList<FsaEstablishment> Establishments(params string[] ratings) =>
        ratings.Select(r => new FsaEstablishment { RatingValue = r }).ToList();
}
