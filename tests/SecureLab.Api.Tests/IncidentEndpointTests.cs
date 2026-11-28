using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class IncidentEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetList_ReturnsSeededIncidents()
    {
        var incidents = await _client.GetFromJsonAsync<List<IncidentListItemResponse>>(
            "/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task GetDetails_ForUnknownId_ReturnsProblemDetails404()
    {
        using var response = await _client.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        using var response = await _client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(document.RootElement.TryGetProperty("ownerUserId", out _));
        Assert.False(document.RootElement.TryGetProperty("email", out _));
        Assert.Equal("Аліса Коваль", document.RootElement.GetProperty("ownerDisplayName").GetString());
    }

    [Fact]
    public async Task ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        var script = await _client.GetStringAsync("/app.js");

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Health_WhenDatabaseIsAvailable_ReturnsReady()
    {
        using var response = await _client.GetAsync("/health");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SeveritySummary_WithoutFilter_ReturnsAllSeveritiesInCriticalityOrder()
    {
        using var response = await _client.GetAsync("/api/incidents/severity-summary");
        var summary = await response.Content.ReadFromJsonAsync<List<IncidentSeveritySummaryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(summary);
        Assert.Equal(["Low", "Medium", "High", "Critical"], summary.Select(item => item.Severity));
        // ЛР 02: scaffold додає два Low-інциденти (…0004, …0005), тому Low = 1 + 2 = 3.
        Assert.Equal([3, 1, 1, 0], summary.Select(item => item.Count));
    }

    [Fact]
    public async Task SeveritySummary_ExposesOnlySeverityAndCount()
    {
        var json = await _client.GetStringAsync("/api/incidents/severity-summary");
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            Assert.Equal(["severity", "count"], item.EnumerateObject().Select(property => property.Name));
        }
    }

    [Fact]
    public async Task SeveritySummary_FilteredByTriaged_CountsOnlyMedium()
    {
        var summary = await _client.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary?status=Triaged");

        Assert.NotNull(summary);
        Assert.Equal([0, 1, 0, 0], summary.Select(item => item.Count));
        Assert.Equal("Medium", summary.Single(item => item.Count > 0).Severity);
    }

    [Fact]
    public async Task SeveritySummary_FilteredByResolved_ReturnsAllZeroCounts()
    {
        var summary = await _client.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary?status=Resolved");

        Assert.NotNull(summary);
        Assert.Equal(4, summary.Count);
        Assert.All(summary, item => Assert.Equal(0, item.Count));
    }

    [Fact]
    public async Task SeveritySummary_WithUnknownStatus_ReturnsValidationProblem400()
    {
        using var response = await _client.GetAsync("/api/incidents/severity-summary?status=Unknown");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("status", out _));
    }
}
