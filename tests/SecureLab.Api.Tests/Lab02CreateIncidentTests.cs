using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

// ЛР 02, етап 2: контракт POST /api/incidents.
// Кожен тест створює лише записи з унікальним title і видаляє їх у finally.
public sealed class Lab02CreateIncidentTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const string Description = "Штучний опис навчального інциденту для перевірки контракту.";

    private readonly HttpClient _client = factory.CreateClient();

    private static string NewTitle(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static object ValidBody(string title, string severity = "Low", string description = Description) => new
    {
        title,
        description,
        severity,
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private async Task DeleteByTitleAsync(params string[] titles)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => titles.Contains(item.Title)).ExecuteDeleteAsync();
    }

    private async Task<int> CountByTitleAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.CountAsync(item => item.Title == title);
    }

    private static async Task<JsonElement> ReadErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        AssertNoInternals(json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("errors").Clone();
    }

    private static void AssertNoInternals(string json)
    {
        Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecureLab.Api", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T01_ValidCreate_Returns201WithLocationAndSafeResponseDto()
    {
        var title = NewTitle("T01");
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", ValidBody("  " + title + "  "));
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var id = root.GetProperty("id").GetGuid();
            Assert.Equal($"/api/incidents/{id}", response.Headers.Location?.OriginalString);
            Assert.Equal(
                ["id", "title", "severity", "status", "occurredAtUtc", "createdAtUtc", "updatedAtUtc"],
                root.EnumerateObject().Select(property => property.Name));
            Assert.Equal(title, root.GetProperty("title").GetString());
            Assert.Equal("Low", root.GetProperty("severity").GetString());
            Assert.Equal("New", root.GetProperty("status").GetString());
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task T02_InvalidDto_Returns400WithAllFieldKeysAndWritesNothing()
    {
        var body = new
        {
            title = "   ",
            description = new string('x', 4001),
            severity = "Urgent",
            occurredAtUtc = (DateTimeOffset?)null
        };

        using var response = await _client.PostAsJsonAsync("/api/incidents", body);
        var errors = await ReadErrorsAsync(response);

        Assert.Equal(
            ["description", "occurredAtUtc", "severity", "title"],
            errors.EnumerateObject().Select(property => property.Name).Order());
    }

    [Theory]
    [InlineData("7")]
    [InlineData("3")]
    [InlineData("Medium,High")]
    [InlineData("")]
    public async Task T02_SeverityOutsideAllowlist_Returns400WithSeverityKeyOnly(string severity)
    {
        var title = NewTitle("T02-sev");
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", ValidBody(title, severity));
            var errors = await ReadErrorsAsync(response);

            Assert.Equal(["severity"], errors.EnumerateObject().Select(property => property.Name));
            Assert.Equal(0, await CountByTitleAsync(title));
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task T02_TitleLongerThan160_Returns400_And160IsAccepted()
    {
        var accepted = NewTitle("T02-len").PadRight(160, 'x');
        var rejected = accepted + "x";
        try
        {
            using var tooLong = await _client.PostAsJsonAsync("/api/incidents", ValidBody(rejected));
            var errors = await ReadErrorsAsync(tooLong);
            Assert.True(errors.TryGetProperty("title", out _));

            using var boundary = await _client.PostAsJsonAsync("/api/incidents", ValidBody(accepted));
            Assert.Equal(HttpStatusCode.Created, boundary.StatusCode);
        }
        finally
        {
            await DeleteByTitleAsync(accepted, rejected);
        }
    }

    [Fact]
    public async Task T02_OccurredAtMoreThan5MinutesInFuture_Returns400()
    {
        var body = new
        {
            title = NewTitle("T02-future"),
            description = Description,
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(6)
        };

        using var response = await _client.PostAsJsonAsync("/api/incidents", body);
        var errors = await ReadErrorsAsync(response);

        Assert.Equal(["occurredAtUtc"], errors.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task T02_OccurredAtWithOffset_IsStoredAsUtc()
    {
        var title = NewTitle("T02-offset");
        var local = DateTimeOffset.UtcNow.AddHours(-1).ToOffset(TimeSpan.FromHours(3));
        var body = new { title, description = Description, severity = "Low", occurredAtUtc = local };
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", body);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var stored = document.RootElement.GetProperty("occurredAtUtc").GetDateTimeOffset();
            Assert.Equal(TimeSpan.Zero, stored.Offset);
            Assert.Equal(local.UtcDateTime, stored.UtcDateTime, TimeSpan.FromMilliseconds(1));
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Theory]
    [InlineData("High", 39, false)]
    [InlineData("High", 40, true)]
    [InlineData("Critical", 39, false)]
    [InlineData("Medium", 39, true)]
    public async Task T09_HighOrCriticalRequires40CharDescription(string severity, int length, bool accepted)
    {
        var title = NewTitle("T09");
        try
        {
            using var response = await _client.PostAsJsonAsync(
                "/api/incidents", ValidBody(title, severity, "  " + new string('x', length) + "  "));

            if (accepted)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
            else
            {
                var errors = await ReadErrorsAsync(response);
                Assert.Equal(["description"], errors.EnumerateObject().Select(property => property.Name));
                Assert.Equal(0, await CountByTitleAsync(title));
            }
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task T03_DuplicateActiveTitle_Returns409WithoutErrors_CaseAndClosedDoNotBlock()
    {
        var title = $"Regression-{Guid.NewGuid():N}";
        var otherCase = title.ToUpperInvariant();
        try
        {
            using var first = await _client.PostAsJsonAsync("/api/incidents", ValidBody(title));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var duplicate = await _client.PostAsJsonAsync("/api/incidents", ValidBody("  " + title + " "));
            var json = await duplicate.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
            Assert.Equal(409, document.RootElement.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("detail").GetString()));
            Assert.False(document.RootElement.TryGetProperty("errors", out _));
            AssertNoInternals(json);
            Assert.Equal(1, await CountByTitleAsync(title));

            using var differentCase = await _client.PostAsJsonAsync("/api/incidents", ValidBody(otherCase));
            Assert.Equal(HttpStatusCode.Created, differentCase.StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
                await db.Incidents.Where(item => item.Title == title)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, IncidentStatus.Closed));
            }

            using var afterClosed = await _client.PostAsJsonAsync("/api/incidents", ValidBody(title));
            Assert.Equal(HttpStatusCode.Created, afterClosed.StatusCode);
            Assert.Equal(2, await CountByTitleAsync(title));
        }
        finally
        {
            await DeleteByTitleAsync(title, otherCase);
        }
    }

    [Theory]
    [InlineData(91, false)]
    [InlineData(89, true)]
    public async Task T10_OccurredAtOlderThan90Days_Returns400(int daysAgo, bool accepted)
    {
        var title = NewTitle("T10");
        var body = new
        {
            title,
            description = Description,
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddDays(-daysAgo)
        };
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", body);
            if (accepted)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
            else
            {
                var errors = await ReadErrorsAsync(response);
                Assert.Equal(["occurredAtUtc"], errors.EnumerateObject().Select(property => property.Name));
            }
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task A02_ServerManagedFieldsInBody_AreIgnored()
    {
        var title = NewTitle("A02");
        var sentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var body = new
        {
            id = sentId,
            ownerUserId = DbSeeder.BobId,
            status = "Closed",
            createdAtUtc = "2000-01-01T00:00:00Z",
            title,
            description = Description,
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var details = await _client.GetFromJsonAsync<JsonElement>(response.Headers.Location);
            Assert.NotEqual(sentId, details.GetProperty("id").GetGuid());
            Assert.Equal("New", details.GetProperty("status").GetString());
            Assert.Equal("Аліса Коваль", details.GetProperty("ownerDisplayName").GetString());
            Assert.True(details.GetProperty("createdAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }
}
