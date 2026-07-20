namespace MiniMonitor.Contracts;

/// <summary>
/// Amostra como a API devolve na leitura. Além do que o agente enviou, traz o
/// id gerado pelo banco e o horário em que a API recebeu o dado.
/// </summary>
/// <param name="ReceivedAtUtc">
/// Momento em que a API recebeu a amostra, carimbado pelo servidor. Existe
/// separado de <paramref name="CapturedAtUtc"/> por dois motivos: o relógio da
/// máquina do agente pode estar errado, e com fila local a amostra pode chegar
/// bem depois de ter sido coletada.
/// </param>
public record ActivitySampleResponse(
    long Id,
    string Hostname,
    string Username,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    string WindowTitle);
