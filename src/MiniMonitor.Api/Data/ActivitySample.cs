namespace MiniMonitor.Api.Data;

/// <summary>
/// Entidade persistida no Postgres. É separada dos records de contrato porque
/// tem campos que só existem do lado do servidor e porque mudança de schema não
/// deve vazar automaticamente para o formato que o agente consome.
/// </summary>
public class ActivitySample
{
    public long Id { get; set; }

    public string Hostname { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>Quando o agente coletou. Carimbado na máquina de origem.</summary>
    public DateTimeOffset CapturedAtUtc { get; set; }

    /// <summary>Quando a API recebeu. Carimbado pelo servidor.</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }

    public string WindowTitle { get; set; } = string.Empty;
}
