namespace MiniMonitor.Contracts;

/// <summary>
/// Uma janela distinta dentro do período consultado, com quantas amostras
/// caíram nela.
/// </summary>
/// <param name="SampleCount">
/// Quantidade de amostras. Como o agente coleta em intervalo fixo, esse número
/// é proporcional ao tempo passado naquela janela.
/// </param>
public record WindowUsageItem(
    string Hostname,
    string WindowTitle,
    int SampleCount,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

/// <summary>
/// Resposta do relatório. Devolve junto o período efetivamente considerado,
/// para quem consome não precisar deduzir o que os parâmetros viraram.
/// </summary>
public record WindowUsageReport(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int TotalSamples,
    IReadOnlyList<WindowUsageItem> Items);
