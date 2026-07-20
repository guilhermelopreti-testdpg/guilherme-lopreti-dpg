using Microsoft.Extensions.Options;
using MiniMonitor.Agent.Collectors;
using MiniMonitor.Agent.Sending;
using MiniMonitor.Contracts;

namespace MiniMonitor.Agent;

public class CollectorWorker(
    IActivityCollector collector,
    MiniMonitorApiClient apiClient,
    IOptions<AgentOptions> options,
    ILogger<CollectorWorker> logger) : BackgroundService
{
    private readonly AgentOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Agente iniciado em {Hostname}. Coletando a cada {Interval}s e enviando para {ApiBaseUrl}.",
            Environment.MachineName,
            _options.IntervalSeconds,
            _options.ApiBaseUrl);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CollectAndSendAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal por Ctrl+C, não é erro.
        }

        logger.LogInformation("Agente encerrado.");
    }

    private async Task CollectAndSendAsync(CancellationToken cancellationToken)
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

        try
        {
            await apiClient.SendAsync(sample, cancellationToken);
            logger.LogInformation("Amostra enviada: {WindowTitle}", windowTitle);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A falha é registrada e o laço continua. O agente nunca cai por
            // causa da API. Neste passo a amostra é perdida, o que a fila
            // local do próximo passo resolve.
            //
            // Loga só a mensagem, não a exceção inteira. Com a API fora isso
            // aconteceria a cada coleta, e a pilha de chamadas de um erro de
            // conexão não acrescenta nada além de poluir o log.
            logger.LogWarning("Falha ao enviar amostra para a API ({Motivo}), seguindo em frente.", ex.Message);
        }
    }
}
