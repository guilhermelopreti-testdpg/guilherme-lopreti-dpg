namespace MiniMonitor.Contracts;

/// <summary>
/// Amostra coletada pelo agente e enviada para a API.
/// Representa o que o agente sabe no momento da coleta, sem nada
/// que seja responsabilidade do servidor (id, horário de recebimento).
/// </summary>
/// <param name="Hostname">Nome da máquina onde o agente está rodando.</param>
/// <param name="Username">Usuário logado na sessão que foi observada.</param>
/// <param name="CapturedAtUtc">
/// Momento da coleta, sempre em UTC. É <see cref="DateTimeOffset"/> e não
/// <see cref="DateTime"/> porque o offset viaja junto no JSON, então a API não
/// precisa adivinhar de que fuso veio o valor.
/// </param>
/// <param name="WindowTitle">
/// Título da janela em primeiro plano. Pode vir vazio quando não há janela
/// ativa, por exemplo com a tela bloqueada ou o foco na área de trabalho.
/// </param>
public record ActivitySampleRequest(
    string Hostname,
    string Username,
    DateTimeOffset CapturedAtUtc,
    string WindowTitle);
