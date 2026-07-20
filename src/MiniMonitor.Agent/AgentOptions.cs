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
}
