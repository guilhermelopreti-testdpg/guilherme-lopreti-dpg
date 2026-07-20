using Microsoft.EntityFrameworkCore;
using MiniMonitor.Api.Data;
using MiniMonitor.Contracts;

namespace MiniMonitor.Api.Endpoints;

public static class SampleEndpoints
{
    /// <summary>Teto de itens por página, para uma leitura não puxar a tabela inteira.</summary>
    private const int MaxPageSize = 200;

    private const int DefaultPageSize = 50;

    public static IEndpointRouteBuilder MapSampleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/samples").WithTags("Samples");

        group.MapPost("/", IngestAsync)
            .WithName("IngestSample")
            .WithSummary("Recebe uma amostra coletada pelo agente")
            .Produces<ActivitySampleResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/", ListAsync)
            .WithName("ListSamples")
            .WithSummary("Lista as amostras mais recentes")
            .Produces<IReadOnlyList<ActivitySampleResponse>>();

        return app;
    }

    private static async Task<IResult> IngestAsync(
        ActivitySampleRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var entity = new ActivitySample
        {
            Hostname = request.Hostname.Trim(),
            Username = request.Username.Trim(),

            // O agente já manda em UTC, mas uma chamada feita direto pelo Swagger
            // pode vir com fuso local. ToUniversalTime normaliza para offset zero,
            // que é o que o Npgsql aceita gravar num timestamptz.
            CapturedAtUtc = request.CapturedAtUtc.ToUniversalTime(),

            // Quem carimba o recebimento é o servidor, nunca o cliente.
            ReceivedAtUtc = DateTimeOffset.UtcNow,

            WindowTitle = request.WindowTitle ?? string.Empty
        };

        db.ActivitySamples.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/samples/{entity.Id}", ToResponse(entity));
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        CancellationToken cancellationToken,
        string? hostname = null,
        int? limit = null,
        int? offset = null)
    {
        var take = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var skip = Math.Max(offset ?? 0, 0);

        // AsNoTracking porque é leitura pura, não precisa do EF vigiando mudanças.
        var query = db.ActivitySamples.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(hostname))
        {
            query = query.Where(s => s.Hostname == hostname);
        }

        var samples = await query
            .OrderByDescending(s => s.CapturedAtUtc)
            .Skip(skip)
            .Take(take)
            // O Select vem antes do ToListAsync de propósito: assim a projeção
            // vira parte do SELECT, em vez de trazer a entidade inteira e
            // converter em memória.
            .Select(s => new ActivitySampleResponse(
                s.Id,
                s.Hostname,
                s.Username,
                s.CapturedAtUtc,
                s.ReceivedAtUtc,
                s.WindowTitle))
            .ToListAsync(cancellationToken);

        return Results.Ok(samples);
    }

    private static Dictionary<string, string[]> Validate(ActivitySampleRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            errors[nameof(request.Hostname)] = ["Hostname é obrigatório."];
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            errors[nameof(request.Username)] = ["Username é obrigatório."];
        }

        if (request.CapturedAtUtc == default)
        {
            errors[nameof(request.CapturedAtUtc)] = ["CapturedAtUtc é obrigatório."];
        }

        return errors;
    }

    private static ActivitySampleResponse ToResponse(ActivitySample entity) =>
        new(entity.Id,
            entity.Hostname,
            entity.Username,
            entity.CapturedAtUtc,
            entity.ReceivedAtUtc,
            entity.WindowTitle);
}
