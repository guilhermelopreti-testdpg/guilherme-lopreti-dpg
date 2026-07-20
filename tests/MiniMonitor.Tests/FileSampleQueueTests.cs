using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiniMonitor.Agent;
using MiniMonitor.Agent.Queueing;
using MiniMonitor.Contracts;

namespace MiniMonitor.Tests;

/// <summary>
/// Testa a fila local do agente, que é onde mora a lógica de resiliência.
/// Cada teste roda sobre um arquivo próprio numa pasta temporária, então eles
/// não interferem entre si e não dependem de banco nem de API no ar.
/// </summary>
public class FileSampleQueueTests : IDisposable
{
    private readonly string _directory;
    private readonly string _queueFilePath;

    public FileSampleQueueTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "minimonitor-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _queueFilePath = Path.Combine(_directory, "pending.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private FileSampleQueue CreateQueue(int maxQueuedSamples = 10_000)
    {
        var options = Options.Create(new AgentOptions
        {
            QueueFilePath = _queueFilePath,
            MaxQueuedSamples = maxQueuedSamples
        });

        return new FileSampleQueue(options, NullLogger<FileSampleQueue>.Instance);
    }

    private static ActivitySampleRequest SampleWith(string windowTitle, int minutesAgo = 0) =>
        new(
            Hostname: "PC-TESTE",
            Username: "guilherme",
            CapturedAtUtc: new DateTimeOffset(2026, 7, 20, 15, 30, 0, TimeSpan.Zero).AddMinutes(-minutesAgo),
            WindowTitle: windowTitle);

    [Fact]
    public async Task Amostra_enfileirada_volta_identica_na_leitura()
    {
        var queue = CreateQueue();
        var original = SampleWith("Visual Studio Code");

        await queue.EnqueueAsync(original, CancellationToken.None);
        var pending = await queue.PeekAsync(10, CancellationToken.None);

        // A comparação direta só funciona porque ActivitySampleRequest é record,
        // que compara por valor e não por referência.
        Assert.Equal(original, Assert.Single(pending));
    }

    [Fact]
    public async Task Horario_em_utc_sobrevive_a_ida_e_volta_do_arquivo()
    {
        var queue = CreateQueue();
        var original = SampleWith("Chrome");

        await queue.EnqueueAsync(original, CancellationToken.None);
        var recuperada = (await queue.PeekAsync(10, CancellationToken.None)).Single();

        Assert.Equal(original.CapturedAtUtc, recuperada.CapturedAtUtc);
        Assert.Equal(TimeSpan.Zero, recuperada.CapturedAtUtc.Offset);
    }

    [Fact]
    public async Task Leitura_preserva_a_ordem_de_chegada()
    {
        var queue = CreateQueue();

        await queue.EnqueueAsync(SampleWith("Primeira"), CancellationToken.None);
        await queue.EnqueueAsync(SampleWith("Segunda"), CancellationToken.None);
        await queue.EnqueueAsync(SampleWith("Terceira"), CancellationToken.None);

        var pending = await queue.PeekAsync(10, CancellationToken.None);

        Assert.Equal(
            ["Primeira", "Segunda", "Terceira"],
            pending.Select(sample => sample.WindowTitle));
    }

    [Fact]
    public async Task Leitura_nao_remove_a_amostra_da_fila()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(SampleWith("Slack"), CancellationToken.None);

        await queue.PeekAsync(10, CancellationToken.None);
        var segundaLeitura = await queue.PeekAsync(10, CancellationToken.None);

        // Esta é a garantia central: enquanto a API não confirmar o
        // recebimento, a amostra continua guardada.
        Assert.Single(segundaLeitura);
        Assert.Equal(1, await queue.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Confirmacao_parcial_remove_so_o_que_foi_enviado()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(SampleWith("Primeira"), CancellationToken.None);
        await queue.EnqueueAsync(SampleWith("Segunda"), CancellationToken.None);
        await queue.EnqueueAsync(SampleWith("Terceira"), CancellationToken.None);

        // Cenário real: a API aceitou duas e caiu na terceira.
        await queue.AcknowledgeAsync(2, CancellationToken.None);

        var restantes = await queue.PeekAsync(10, CancellationToken.None);

        Assert.Equal("Terceira", Assert.Single(restantes).WindowTitle);
    }

    [Fact]
    public async Task Fila_sobrevive_ao_agente_ser_reiniciado()
    {
        var primeiraExecucao = CreateQueue();
        await primeiraExecucao.EnqueueAsync(SampleWith("Antes do reinício"), CancellationToken.None);

        // Uma instância nova sobre o mesmo arquivo representa o agente sendo
        // fechado e aberto de novo. É isso que uma fila em memória perderia.
        var segundaExecucao = CreateQueue();
        var pending = await segundaExecucao.PeekAsync(10, CancellationToken.None);

        Assert.Equal("Antes do reinício", Assert.Single(pending).WindowTitle);
    }

    [Fact]
    public async Task Fila_cheia_descarta_as_amostras_mais_antigas()
    {
        var queue = CreateQueue(maxQueuedSamples: 10);

        for (var i = 0; i < 13; i++)
        {
            await queue.EnqueueAsync(SampleWith($"Janela {i}"), CancellationToken.None);
        }

        var pending = await queue.PeekAsync(100, CancellationToken.None);

        Assert.Equal(10, pending.Count);
        // As três primeiras saíram, sobraram as dez mais recentes.
        Assert.Equal("Janela 3", pending[0].WindowTitle);
        Assert.Equal("Janela 12", pending[^1].WindowTitle);
    }

    [Fact]
    public async Task Confirmar_tudo_esvazia_a_fila()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(SampleWith("Única"), CancellationToken.None);

        await queue.AcknowledgeAsync(1, CancellationToken.None);

        Assert.Equal(0, await queue.CountAsync(CancellationToken.None));
        Assert.Empty(await queue.PeekAsync(10, CancellationToken.None));
    }
}
