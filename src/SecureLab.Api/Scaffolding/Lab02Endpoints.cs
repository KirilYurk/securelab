using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    private const int TitleMaxLength = 160;
    private const int DescriptionMaxLength = 4000;
    private const int HighSeverityDescriptionMinLength = 40;
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReportingWindow = TimeSpan.FromDays(90);

    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", SearchAsync)
            .WithName("SearchIncidents")
            .Produces<List<IncidentSearchItemResponse>>()
            .ProducesValidationProblem();
        app.MapPost("/api/incidents", CreateIncidentAsync)
            .WithName("CreateIncident")
            .Produces<CreatedIncidentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private const int SearchRowLimit = 50;
    private static readonly string[] AllowedSortBy = ["createdAtUtc", "severity", "status"];

    private static async Task<IResult> SearchAsync(
        string? q,
        string? sortBy,
        SecureLabDbContext db,
        CancellationToken ct)
    {
        // Структура запиту (ORDER BY) — лише з серверного allowlist; невідоме значення
        // не доходить до БД. Назву стовпця параметризувати неможливо, тому тільки allowlist.
        if (!string.IsNullOrEmpty(sortBy) && !AllowedSortBy.Contains(sortBy, StringComparer.Ordinal))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
            });
        }

        // Значення q — лише дані: передається в БД окремим параметром @pattern,
        // а EscapeLike робить %, _ і \ літеральними символами.
        var pattern = EscapeLike(q ?? "");
        var query = db.Incidents
            .AsNoTracking()
            .Where(item => EF.Functions.ILike(item.Title, pattern, "\\")
                || EF.Functions.ILike(item.Description, pattern, "\\"));

        // Ранги замість алфавітного порядку рядкових значень enum (EF → CASE WHEN).
        var ordered = sortBy switch
        {
            "severity" => query
                .OrderBy(item => item.Severity == IncidentSeverity.Critical ? 0
                    : item.Severity == IncidentSeverity.High ? 1
                    : item.Severity == IncidentSeverity.Medium ? 2
                    : 3)
                .ThenBy(item => item.Id),
            "status" => query
                .OrderBy(item => item.Status == IncidentStatus.New ? 0
                    : item.Status == IncidentStatus.Triaged ? 1
                    : item.Status == IncidentStatus.InProgress ? 2
                    : item.Status == IncidentStatus.Resolved ? 3
                    : 4)
                .ThenBy(item => item.Id),
            _ => query
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
        };

        var items = await ordered
            .Take(SearchRowLimit)
            .Select(item => new IncidentSearchItemResponse(
                item.Id,
                item.Title,
                item.Description,
                item.Severity.ToString(),
                item.Status.ToString(),
                item.CreatedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    // Пошук підрядка як літерала: спершу \, потім _ і %, далі обгортка %…%.
    // Апостроф не екранується — це звичайне значення параметра.
    public static string EscapeLike(string value) =>
        "%" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
        + "%";

    private static async Task<IResult> CreateIncidentAsync(
        CreateIncidentRequest request,
        SecureLabDbContext db,
        CancellationToken ct)
    {
        // Один момент часу на весь запит: однакова межа для всіх перевірок і для збереження.
        var now = DateTimeOffset.UtcNow;
        var errors = new Dictionary<string, string[]>();

        // 1. Title: обов'язковий після Trim(), сирий рядок не довший за 160.
        if (string.IsNullOrWhiteSpace(request.Title))
            errors["title"] = ["Назва обов'язкова."];
        else if (request.Title.Length > TitleMaxLength)
            errors["title"] = [$"Назва не може бути довшою за {TitleMaxLength} символів."];

        // 2. Description: обов'язковий після Trim(), сирий рядок не довший за 4000.
        if (string.IsNullOrWhiteSpace(request.Description))
            errors["description"] = ["Опис обов'язковий."];
        else if (request.Description.Length > DescriptionMaxLength)
            errors["description"] = [$"Опис не може бути довшим за {DescriptionMaxLength} символів."];

        // 3. Severity: TryParse + IsDefined, без мовчазної підміни на Low.
        //    Додатково вимагаємо саме назву значення: інакше "3" або "Medium,High"
        //    пройшли б TryParse/IsDefined і перетворилися б на Critical.
        var severityIsValid =
            Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
            && Enum.IsDefined(severity)
            && string.Equals(severity.ToString(), request.Severity?.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!severityIsValid)
            errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

        // 4. OccurredAtUtc: обов'язкова, не пізніше now + 5 хв;
        //    T-10 (власне правило): не раніше now − 90 днів.
        if (request.OccurredAtUtc is null)
            errors["occurredAtUtc"] = ["Час інциденту обов'язковий."];
        else if (request.OccurredAtUtc.Value > now + FutureTolerance)
            errors["occurredAtUtc"] = ["Час інциденту не може бути в майбутньому (допуск 5 хвилин)."];
        else if (request.OccurredAtUtc.Value < now - ReportingWindow)
            errors["occurredAtUtc"] = ["Інцидент старший за 90 днів реєструється через ретроспективний розбір, а не цією формою."];

        // 5. Нормалізація один раз; далі працюємо лише з цими значеннями.
        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";

        // 6. Cross-field (T-09): для High/Critical опис після Trim() ≥ 40 символів.
        //    Лише якщо severity коректна і description ще не має власної помилки.
        if (severityIsValid
            && severity is IncidentSeverity.High or IncidentSeverity.Critical
            && !errors.ContainsKey("description")
            && description.Length < HighSeverityDescriptionMinLength)
        {
            errors["description"] =
                [$"Для High/Critical опис має містити щонайменше {HighSeverityDescriptionMinLength} символів."];
        }

        // Усі помилки разом і до будь-якого доступу до БД.
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        // 7. Предметний конфлікт (T-03): той самий title (з урахуванням регістру)
        //    в активному статусі. Closed повторне створення не блокує.
        var duplicateExists = await db.Incidents.AnyAsync(
            item => item.Title == title
                && (item.Status == IncidentStatus.New
                    || item.Status == IncidentStatus.Triaged
                    || item.Status == IncidentStatus.InProgress
                    || item.Status == IncidentStatus.Resolved),
            ct);
        if (duplicateExists)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Інцидент уже зареєстровано",
                detail: "Активний інцидент з такою самою назвою вже існує. Змініть назву або дочекайтеся закриття наявного інциденту.");
        }

        // 8. Серверні поля: Id, власник, статус і часи клієнт не задає.
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            OwnerUserId = DbSeeder.AliceId,
            Title = title,
            Description = description,
            Severity = severity,
            Status = IncidentStatus.New,
            OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/incidents/{incident.Id}",
            new CreatedIncidentResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.UpdatedAtUtc));
    }
}

// Request DTO: лише поля, які клієнт має право передати.
// Id, OwnerUserId, Status, CreatedAtUtc, UpdatedAtUtc навмисно відсутні.
public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

// Елемент результату пошуку: та сама форма, що й у стартовому search.
public sealed record IncidentSearchItemResponse(
    Guid Id,
    string Title,
    string Description,
    string Severity,
    string Status,
    DateTimeOffset CreatedAtUtc);

// Response DTO для 201 Created: без OwnerUserId та інших внутрішніх полів entity.
public sealed record CreatedIncidentResponse(
    Guid Id,
    string Title,
    string Severity,
    string Status,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
