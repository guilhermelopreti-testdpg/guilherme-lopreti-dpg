using MiniMonitor.Contracts;

namespace MiniMonitor.Agent.Queueing;

/// <summary>
/// Fila local de amostras pendentes de envio. O modelo é de fila com
/// confirmação: quem consome lê um lote, envia, e só então confirma. Enquanto
/// não houver confirmação, as amostras continuam guardadas.
/// </summary>
public interface ISampleQueue
{
    /// <summary>Guarda a amostra no fim da fila.</summary>
    Task EnqueueAsync(ActivitySampleRequest sample, CancellationToken cancellationToken);

    /// <summary>
    /// Lê até <paramref name="maxItems"/> amostras do início da fila sem
    /// removê-las. Ordem de chegada preservada.
    /// </summary>
    Task<IReadOnlyList<ActivitySampleRequest>> PeekAsync(int maxItems, CancellationToken cancellationToken);

    /// <summary>
    /// Remove definitivamente as <paramref name="count"/> primeiras amostras.
    /// Chamado só depois que o envio delas foi confirmado pela API.
    /// </summary>
    Task AcknowledgeAsync(int count, CancellationToken cancellationToken);

    /// <summary>Quantas amostras estão pendentes.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);
}
