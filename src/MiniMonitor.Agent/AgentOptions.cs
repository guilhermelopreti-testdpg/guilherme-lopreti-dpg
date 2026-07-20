using System.ComponentModel.DataAnnotations;

namespace MiniMonitor.Agent;

public class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Endereço base da API central.</summary>
    [Required]
    public string ApiBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>Intervalo entre coletas, em segundos.</summary>
    [Range(1, 3600)]
    public int IntervalSeconds { get; set; } = 5;

    /// <summary>Tempo limite de cada chamada à API.</summary>
    [Range(1, 120)]
    public int HttpTimeoutSeconds { get; set; } = 10;

    /// <summary>Arquivo da fila local de amostras pendentes.</summary>
    [Required]
    public string QueueFilePath { get; set; } = "queue/pending.jsonl";

    /// <summary>
    /// Teto da fila local. Ao estourar, as amostras mais antigas são
    /// descartadas, para a API fora do ar por dias não encher o disco.
    /// </summary>
    [Range(10, 1_000_000)]
    public int MaxQueuedSamples { get; set; } = 10_000;

    /// <summary>Quantas amostras tentar enviar por ciclo.</summary>
    [Range(1, 500)]
    public int DrainBatchSize { get; set; } = 50;
}
