using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

// ЛР 02, етап 4: security regression для GET /api/incidents/search.
// Очікування спираються на seed після --reset-database (5 інцидентів …0001–…0005).
public sealed class Lab02SearchTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static Guid SeedId(int number) => Guid.Parse($"20000000-0000-0000-0000-{number:000000000000}");

    private async Task<List<IncidentSearchItemResponse>> SearchAsync(string query)
    {
        using var response = await _client.GetAsync("/api/incidents/search" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var items = await response.Content.ReadFromJsonAsync<List<IncidentSearchItemResponse>>();
        Assert.NotNull(items);
        return items;
    }

    private Task<List<IncidentSearchItemResponse>> SearchByQAsync(string value) =>
        SearchAsync("?q=" + Uri.EscapeDataString(value));

    [Fact]
    public async Task S02_PositiveControl_UsbReturnsExactlyIncident0005()
    {
        var items = await SearchByQAsync("USB");

        var item = Assert.Single(items);
        Assert.Equal(SeedId(5), item.Id);
    }

    [Fact]
    public async Task S02_ControlInputFromStage3_ReturnsEmptyResultSet()
    {
        // Той самий дозволений контрольний ввід C, що до виправлення повертав усі 5 записів.
        var items = await SearchByQAsync("zz-no-match' OR TRUE -- ");

        Assert.Empty(items);
    }

    [Fact]
    public async Task T04_ApostropheInLatin_ReturnsOBrienRecord()
    {
        var items = await SearchByQAsync("O'Brien");

        var item = Assert.Single(items);
        Assert.Equal(SeedId(4), item.Id);
        Assert.Equal("O'Brien classroom report", item.Title);
    }

    [Fact]
    public async Task T04_ApostropheInCyrillic_ReturnsClassroomLogRecord()
    {
        var items = await SearchByQAsync("комп'ютерного");

        var item = Assert.Single(items);
        Assert.Equal(SeedId(3), item.Id);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task T04_LikeWildcardsAreLiterals_ReturnEmpty(string value)
    {
        Assert.Empty(await SearchByQAsync(value));
    }

    [Fact]
    public async Task T04_SearchMatchesDescriptionToo()
    {
        // "вкладення" є лише в description інциденту …0001 ("Вкладення не відкривалося").
        var items = await SearchByQAsync("Вкладення не");

        Assert.Equal([SeedId(1)], items.Select(item => item.Id));
    }

    [Fact]
    public async Task T05_UnknownSortBy_Returns400WithSortByKey()
    {
        using var response = await _client.GetAsync("/api/incidents/search?sortBy=price");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = document.RootElement.GetProperty("errors");
        Assert.Equal(["sortBy"], errors.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", new[] { 4, 5, 3, 2, 1 })]
    [InlineData("?sortBy=createdAtUtc", new[] { 4, 5, 3, 2, 1 })]
    [InlineData("?sortBy=severity", new[] { 2, 1, 3, 4, 5 })]
    [InlineData("?sortBy=status", new[] { 3, 4, 5, 1, 2 })]
    public async Task Sorting_FollowsContractOrder(string query, int[] expected)
    {
        var items = await SearchAsync(query);

        Assert.Equal(expected.Select(SeedId), items.Select(item => item.Id));
    }

    [Theory]
    [InlineData("USB", "%USB%")]
    [InlineData("rate_5%", "%rate\\_5\\%%")]
    [InlineData("path\\A", "%path\\\\A%")]
    [InlineData("O'Brien", "%O'Brien%")]
    [InlineData("", "%%")]
    public void EscapeLike_MakesWildcardsLiteral(string input, string expected)
    {
        Assert.Equal(expected, Lab02Endpoints.EscapeLike(input));
    }
}
