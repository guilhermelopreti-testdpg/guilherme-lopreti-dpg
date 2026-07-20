using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiniMonitor.Contracts;

namespace MiniMonitor.Agent.Queueing;

/// <summary>
/// Fila persistida em arquivo no formato JSON Lines, uma amostra por linha.
/// O formato foi escolhido porque permite acrescentar no fim sem reescrever o
/// arquivo inteiro, ao contrário de um array JSON.
/// </summary>
public class FileSampleQueue : ISampleQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Sem indentação: cada amostra precisa caber em exatamente uma linha.
        WriteIndented = false
    };

    // UTF-8 sem BOM. O Utf8NoBom padrão do .NET escreve a marca de ordem de
    // bytes no início do arquivo, o que sujaria a primeira linha para qualquer
    // ferramenta que leia o .jsonl linha a linha.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // Só existe um worker consumindo, mas o semáforo evita que uma futura
    // mudança introduza corrupção de arquivo sem ninguém perceber.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly string _filePath;
    private readonly int _maxItems;
    private readonly ILogger<FileSampleQueue> _logger;

    public FileSampleQueue(IOptions<AgentOptions> options, ILogger<FileSampleQueue> logger)
    {
        _logger = logger;
        _maxItems = options.Value.MaxQueuedSamples;
        _filePath = Path.GetFullPath(options.Value.QueueFilePath);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task EnqueueAsync(ActivitySampleRequest sample, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(sample, JsonOptions);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_filePath, line + Environment.NewLine, Utf8NoBom, cancellationToken);
            await TrimIfNeededAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ActivitySampleRequest>> PeekAsync(int maxItems, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var lines = await ReadLinesAsync(cancellationToken);
            var samples = new List<ActivitySampleRequest>(Math.Min(maxItems, lines.Count));

            foreach (var line in lines.Take(maxItems))
            {
                var sample = Deserialize(line);
                if (sample is null)
                {
                    // Linha corrompida, provavelmente por queda no meio da
                    // escrita. Parar aqui preserva a ordem e deixa a limpeza
                    // para o Acknowledge.
                    break;
                }

                samples.Add(sample);
            }

            return samples;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AcknowledgeAsync(int count, CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var lines = await ReadLinesAsync(cancellationToken);
            var remaining = lines.Skip(count).ToList();

            await WriteAllLinesAtomicallyAsync(remaining, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadLinesAsync(cancellationToken)).Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<string>> ReadLinesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(_filePath, Utf8NoBom, cancellationToken);
        return lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
    }

    /// <summary>
    /// Escreve num arquivo temporário e substitui o original de uma vez. Uma
    /// queda no meio da gravação deixa o arquivo antigo intacto, em vez de um
    /// arquivo pela metade.
    /// </summary>
    private async Task WriteAllLinesAtomicallyAsync(List<string> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            File.Delete(_filePath);
            return;
        }

        var tempPath = _filePath + ".tmp";
        await File.WriteAllLinesAsync(tempPath, lines, Utf8NoBom, cancellationToken);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// Impede a fila de crescer sem limite se a API ficar fora por muito tempo.
    /// Descarta as amostras mais antigas, porque num monitor o dado recente vale
    /// mais que o antigo.
    /// </summary>
    private async Task TrimIfNeededAsync(CancellationToken cancellationToken)
    {
        var lines = await ReadLinesAsync(cancellationToken);
        if (lines.Count <= _maxItems)
        {
            return;
        }

        var dropped = lines.Count - _maxItems;
        await WriteAllLinesAtomicallyAsync(lines.Skip(dropped).ToList(), cancellationToken);

        _logger.LogWarning(
            "Fila local atingiu o limite de {Max} amostras, {Dropped} amostras antigas foram descartadas.",
            _maxItems,
            dropped);
    }

    private ActivitySampleRequest? Deserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<ActivitySampleRequest>(line, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Linha inválida na fila local foi ignorada: {Motivo}", ex.Message);
            return null;
        }
    }
}
