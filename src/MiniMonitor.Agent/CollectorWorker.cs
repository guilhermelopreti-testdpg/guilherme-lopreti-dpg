using Microsoft.Extensions.Options;
using MiniMonitor.Agent.Collectors;
using MiniMonitor.Agent.Queueing;
using MiniMonitor.Agent.Sending;
using MiniMonitor.Contracts;

namespace MiniMonitor.Agent;

public class CollectorWorker(
    IActivityCollector collector,
    ISampleQueue queue,
    MiniMonitorApiClient apiClient,
    IOptions<AgentOptions> options,
    ILogger<CollectorWorker> logger) : BackgroundService
{
    private readonly AgentOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = await queue.CountAsync(stoppingToken);

        logger.LogInformation(
            "Agente iniciado em {Hostname}. Coletando a cada {Interval}s, enviando para {ApiBaseUrl}. Fila local com {Pending} amostras pendentes.",
            Environment.MachineName,
            _options.IntervalSeconds,
            _options.ApiBaseUrl,
            pending);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CollectAsync(stoppingToken);
                await DrainAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal por Ctrl+C, não é erro.
        }

        logger.LogInformation("Agente encerrado.");
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        var windowTitle = collector.GetForegroundWindowTitle();

        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            logger.LogDebug("Nenhuma janela ativa nesta coleta, amostra ignorada.");
            return;
        }

        var sample = new ActivitySampleRequest(
            Hostname: Environment.MachineName,
            Username: Environment.UserName,
            // UtcNow, nunca Now. O horário é carimbado aqui porque este é o
            // momento real do evento, independente de quando o envio acontecer.
            CapturedAtUtc: DateTimeOffset.UtcNow,
            WindowTitle: windowTitle);

        // Grava antes de tentar enviar. Se fosse o contrário, uma queda entre a
        // coleta e o envio perderia a amostra.
        await queue.EnqueueAsync(sample, cancellationToken);
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        var batch = await queue.PeekAsync(_options.DrainBatchSize, cancellationToken);
        if (batch.Count == 0)
        {
            return;
        }

        var sent = 0;

        foreach (var sample in batch)
        {
            try
            {
                await apiClient.SendAsync(sample, cancellationToken);
                sent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Para no primeiro erro para não furar a ordem da fila. O que
                // já foi enviado é confirmado abaixo, o resto fica para o
                // próximo ciclo.
                logger.LogWarning(
                    "Falha ao enviar amostra para a API ({Motivo}). {Pending} amostras seguem na fila local.",
                    ex.Message,
                    batch.Count - sent);
                break;
            }
        }

        if (sent > 0)
        {
            // Só remove do disco depois da confirmação da API.
            await queue.AcknowledgeAsync(sent, cancellationToken);
            logger.LogInformation("{Sent} amostra(s) enviada(s) e removida(s) da fila local.", sent);
        }
    }
}