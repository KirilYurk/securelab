using System.Diagnostics;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents");

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var parsedStatus, out var validationProblem))
        {
            return validationProblem;
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.GetDetailsAsync(id, cancellationToken);
        return incident is null
            ? Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(incident);
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        IncidentQueries queries,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var parsedStatus, out var validationProblem))
        {
            return validationProblem;
        }

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        return Results.Ok(await queries.GetSeveritySummaryAsync(parsedStatus, traceId, cancellationToken));
    }

    private static bool TryParseStatus(
        string? status,
        out IncidentStatus? parsedStatus,
        out IResult validationProblem)
    {
        parsedStatus = null;
        validationProblem = Results.Empty;
        if (status is null)
        {
            return true;
        }

        if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
            || !Enum.IsDefined(candidate))
        {
            validationProblem = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
            });
            return false;
        }

        parsedStatus = candidate;
        return true;
    }
}
