using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MiniMonitor.Agent.Collectors;

/// <summary>
/// Implementação para Windows. Usa a API nativa user32.dll via P/Invoke, que é
/// a forma de chamar código C não gerenciado a partir do .NET.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsActivityCollector : IActivityCollector
{
    /// <summary>
    /// Handle da janela que está em primeiro plano. Retorna
    /// <see cref="IntPtr.Zero"/> quando nenhuma janela tem o foco.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>Tamanho do título, em caracteres, sem contar o terminador nulo.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    /// <summary>
    /// Copia o título para o buffer informado e devolve quantos caracteres
    /// foram escritos. Com CharSet.Unicode o runtime resolve para a variante
    /// GetWindowTextW, que é a que lida com acento e caractere fora do ASCII.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public string? GetForegroundWindowTitle()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            // Janela sem título, o que é comum em janelas de sistema.
            return null;
        }

        // O buffer precisa de espaço para o terminador nulo do lado nativo,
        // por isso o +1.
        var buffer = new StringBuilder(length + 1);
        var written = GetWindowText(handle, buffer, buffer.Capacity);

        return written > 0 ? buffer.ToString() : null;
    }
}
