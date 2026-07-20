namespace MiniMonitor.Agent.Collectors;

/// <summary>
/// Isola a única parte da coleta que depende do sistema operacional.
/// Hostname, usuário e horário o .NET resolve igual em qualquer plataforma,
/// então ficam fora daqui de propósito, para a superfície específica de SO ser
/// a menor possível.
/// </summary>
public interface IActivityCollector
{
    /// <summary>
    /// Título da janela em primeiro plano, ou <c>null</c> quando não há janela
    /// ativa. Isso acontece de verdade, por exemplo com a tela bloqueada, com o
    /// foco na área de trabalho ou quando a janela ativa pertence a um processo
    /// de outro nível de privilégio.
    /// </summary>
    string? GetForegroundWindowTitle();
}
