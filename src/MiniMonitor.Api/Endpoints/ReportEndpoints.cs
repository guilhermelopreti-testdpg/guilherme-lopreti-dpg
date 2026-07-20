using Microsoft.EntityFrameworkCore;
using MiniMonitor.Api.Data;
using MiniMonitor.Contracts;

namespace MiniMonitor.Api.Endpoints;

public static class ReportEndpoints
{
    private const int MaxItems = 100;
    private const int DefaultItems = 20;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(1);

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reports/top-windows", TopWindowsAsync)
            .WithTags("Reports")
            .WithName("TopWindows")
            .WithSummary("Janelas mais frequentes por máquina num período")
            .Produces<WindowUsageReport>();

        return app;
    }

    private static async Task<IResult> TopWindowsAsync(
        AppDbContext db,
        CancellationToken cancellationToken,
        DateTimeOffset? since = null,
        DateTimeOffset? until = null,
        string? hostname = null,
        int? limit = null)
    {
        var to = (until ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var from = (since ?? to - DefaultWindow).ToUniversalTime();

        if (from >= to)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["since"] = ["O início do período precisa ser anterior ao fim."]
            });
        }

        var take = Math.Clamp(limit ?? DefaultItems, 1, MaxItems);

        // Filtro comum às duas consultas. Intervalo fechado no início e aberto
        // no fim, para uma amostra na virada da hora não ser contada duas vezes
        // em relatórios de horas consecutivas.
        var filtered = db.ActivitySamples
            .AsNoTracking()
            .Where(s => s.CapturedAtUtc >= from && s.CapturedAtUtc < to);

        if (!string.IsNullOrWhiteSpace(hostname))
        {
            filtered = filtered.Where(s => s.Hostname == hostname);
        }

        // O GroupBy é traduzido para GROUP BY no Postgres. O banco devolve uma
        // linha por janela distinta, não uma por amostra. Nada de ToListAsync
        // antes daqui, senão a agregação aconteceria em memória.
        //
        // A projeção é para tipo anônimo, e não direto para WindowUsageItem,
        // porque o EF Core 8 não traduz construtor com parâmetros nessa posição.
        var grouped = await filtered
            .GroupBy(s => new { s.Hostname, s.WindowTitle })
            .Select(g => new
            {
                g.Key.Hostname,
                g.Key.WindowTitle,
                SampleCount = g.Count(),
                FirstSeenUtc = g.Min(s => s.CapturedAtUtc),
                LastSeenUtc = g.Max(s => s.CapturedAtUtc)
            })
            .OrderByDescending(x => x.SampleCount)
            .ThenBy(x => x.WindowTitle)
            .Take(take)
            .ToListAsync(cancellationToken);

        // A conversão para o contrato acontece em memória, mas só sobre as
        // linhas já agregadas e limitadas pelo take, no máximo 100.
        var items = grouped
            .Select(x => new WindowUsageItem(
                x.Hostname,
                x.WindowTitle,
                x.SampleCount,
                x.FirstSeenUtc,
                x.LastSeenUtc))
            .ToList();

        // Consulta separada porque o total do período não pode sair da soma dos
        // itens, já que a lista é limitada pelo take.
        var totalSamples = await filtered.CountAsync(cancellationToken);

        return Results.Ok(new WindowUsageReport(from, to, totalSamples, items));
    }
}
