using System.Net.Http.Json;
using MiniMonitor.Contracts;

namespace MiniMonitor.Agent.Sending;

/// <summary>
/// Cliente tipado da API. Registrado com AddHttpClient, o que faz o
/// HttpClient ser gerenciado pela fábrica em vez de instanciado na mão.
/// </summary>
public class MiniMonitorApiClient(HttpClient httpClient)
{
    public async Task SendAsync(ActivitySampleRequest sample, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("/api/samples", sample, cancellationToken);

        // Lança em qualquer status fora da faixa 2xx. Quem chama decide o que
        // fazer com a falha, aqui não engolimos o erro.
        response.EnsureSuccessStatusCode();
    }
}
