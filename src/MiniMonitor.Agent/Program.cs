using Microsoft.Extensions.Options;
using MiniMonitor.Agent;
using MiniMonitor.Agent.Collectors;
using MiniMonitor.Agent.Queueing;
using MiniMonitor.Agent.Sending;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .ValidateDataAnnotations()
    // ValidateOnStart faz configuração inválida derrubar o agente na
    // inicialização, com mensagem clara, em vez de falhar no meio do laço.
    .ValidateOnStart();

// A escolha da implementação de coleta acontece aqui, na composição, e não
// dentro do worker. O worker só conhece a interface.
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IActivityCollector, WindowsActivityCollector>();
}
else
{
    throw new PlatformNotSupportedException(
        "Só existe implementação de coleta para Windows. Veja o DECISIONS.md para o plano de Linux e macOS.");
}

builder.Services.AddSingleton<ISampleQueue, FileSampleQueue>();

builder.Services.AddHttpClient<MiniMonitorApiClient>((serviceProvider, client) =>
{
    var agentOptions = serviceProvider.GetRequiredService<IOptions<AgentOptions>>().Value;
    client.BaseAddress = new Uri(agentOptions.ApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(agentOptions.HttpTimeoutSeconds);
});

builder.Services.AddHostedService<CollectorWorker>();

var host = builder.Build();
host.Run();
