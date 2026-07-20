# Mini-Monitor

Um agente roda na máquina, captura o **título da janela ativa** a cada X segundos e envia para
uma **API central**, que grava num **PostgreSQL**. Um endpoint de **relatório** transforma essas
amostras em informação, respondendo em quais janelas cada máquina passou o tempo.

Se a API estiver fora do ar, o agente continua coletando e guarda tudo numa **fila local em
disco**, que sobrevive inclusive ao agente ser fechado. Quando a API volta, as amostras
represadas são enviadas.

As decisões de projeto e os trade-offs estão no [DECISIONS.md](DECISIONS.md).

---

## Pré-requisitos

| Ferramenta | Versão | Como conferir |
|---|---|---|
| .NET SDK | 8.0 | `dotnet --list-sdks` |
| Docker | qualquer recente, com Compose | `docker --version` |

O agente usa a API `user32.dll` do Windows para ler a janela ativa, então **ele só roda no
Windows**. A API, o banco e os testes rodam em qualquer sistema operacional. O plano para Linux
e macOS está descrito no [DECISIONS.md](DECISIONS.md).

---

## Rodando do zero

Você vai precisar de **dois terminais**, um para a API e outro para o agente.

### 1. Suba o PostgreSQL

Na raiz do repositório:

```powershell
docker compose up -d
```

Espere o banco ficar pronto de verdade. O container aparece como `Up` alguns segundos antes de
aceitar conexão, e o healthcheck existe justamente para você saber a diferença:

```powershell
docker compose ps
```

Siga quando a coluna de status mostrar `Up (healthy)`.

### 2. Rode a API (terminal 1)

```powershell
dotnet run --project src/MiniMonitor.Api --launch-profile http
```

A API sobe em **http://localhost:5080** e aplica as migrations sozinha, então a tabela é criada
na primeira execução. Não precisa rodar nenhum comando de banco.

Abra o Swagger para confirmar: **http://localhost:5080/swagger**

### 3. Rode o agente (terminal 2)

```powershell
dotnet run --project src/MiniMonitor.Agent
```

Ele começa a coletar a cada 5 segundos. Troque de janela algumas vezes para gerar dados
variados. A saída fica assim:

```
info: Agente iniciado em SEU-PC. Coletando a cada 5s, enviando para http://localhost:5080. Fila local com 0 amostras pendentes.
info: 1 amostra(s) enviada(s) e removida(s) da fila local.
```

### 4. Veja os dados

Pelo navegador, no Swagger, ou pelo terminal:

```powershell
curl.exe "http://localhost:5080/api/samples?limit=5"
```

> No PowerShell, use `curl.exe` com o `.exe`. Escrever só `curl` aciona um apelido para
> `Invoke-WebRequest`, que tem outra sintaxe e não funciona com estes exemplos.

---

## Endpoints

### `POST /api/samples` — ingestão

O que o agente chama. Corpo esperado:

```json
{
  "hostname": "PC-TESTE",
  "username": "guilherme",
  "capturedAtUtc": "2026-07-20T15:30:00+00:00",
  "windowTitle": "Visual Studio Code"
}
```

Responde `201 Created`, ou `400` com os erros caso hostname ou username venham vazios.

### `GET /api/samples` — leitura

Amostras mais recentes primeiro.

| Parâmetro | Padrão | Descrição |
|---|---|---|
| `hostname` | todas | filtra por máquina |
| `limit` | 50 | itens por página, teto de 200 |
| `offset` | 0 | quantos pular |

### `GET /api/reports/top-windows` — relatório

Responde **em quais janelas cada máquina passou o tempo no período**. Como o agente coleta em
intervalo fixo, a contagem de amostras é proporcional ao tempo gasto.

| Parâmetro | Padrão | Descrição |
|---|---|---|
| `since` | 1 hora atrás | início do período, em UTC |
| `until` | agora | fim do período, em UTC |
| `hostname` | todas | filtra por máquina |
| `limit` | 20 | janelas retornadas, teto de 100 |

```powershell
curl.exe "http://localhost:5080/api/reports/top-windows"
```

```json
{
  "fromUtc": "2026-07-20T16:22:47+00:00",
  "toUtc": "2026-07-20T17:22:47+00:00",
  "totalSamples": 40,
  "items": [
    { "hostname": "DPG", "windowTitle": "Visual Studio Code", "sampleCount": 20,
      "firstSeenUtc": "2026-07-20T16:41:43+00:00", "lastSeenUtc": "2026-07-20T17:19:40+00:00" }
  ]
}
```

A agregação acontece no banco, com `GROUP BY`. O `totalSamples` vem de uma consulta separada,
porque a lista de itens é cortada pelo `limit` e somá-la daria um número menor que o real.

---

## Testando na mão

### A resiliência: API fora não derruba o agente

Este é o cenário mais importante do projeto. Reproduza assim:

**1.** Com a API **desligada** (feche o terminal 1 com `Ctrl+C`), rode só o agente:

```powershell
dotnet run --project src/MiniMonitor.Agent
```

Ele não trava nem quebra, só avisa a cada ciclo:

```
warn: Falha ao enviar amostra para a API (...). 3 amostras seguem na fila local.
```

**2.** Deixe uns 30 segundos e confira a fila crescendo no disco:

```powershell
Get-Content src/MiniMonitor.Agent/queue/pending.jsonl
```

Uma amostra em JSON por linha.

**3.** Agora **feche o agente** com `Ctrl+C`. O arquivo continua lá, e é isso que uma fila em
memória teria perdido.

**4.** Suba a API e depois o agente de novo. Na inicialização ele informa quantas amostras
estavam guardadas, e drena tudo:

```
info: Agente iniciado em SEU-PC. ... Fila local com 6 amostras pendentes.
info: 7 amostra(s) enviada(s) e removida(s) da fila local.
```

**5.** A prova final está nos dados. Compare os dois horários de uma amostra represada:

```powershell
curl.exe "http://localhost:5080/api/samples?limit=10"
```

As amostras coletadas com a API fora têm uma diferença grande entre `capturedAtUtc` e
`receivedAtUtc`, do tamanho do tempo que ficaram na fila. As coletadas com a API no ar têm
poucos milissegundos de diferença. Os dois campos existem exatamente para isso.

### Os timestamps em UTC

A API normaliza qualquer fuso recebido. Mande um horário de Brasília:

```powershell
curl.exe -X POST http://localhost:5080/api/samples -H "Content-Type: application/json" -d "{\"hostname\":\"PC-TESTE\",\"username\":\"teste\",\"capturedAtUtc\":\"2026-07-20T12:30:00-03:00\",\"windowTitle\":\"Chrome\"}"
```

A resposta traz `2026-07-20T15:30:00+00:00`. Mesmo instante, convertido para UTC.

## Testes automatizados

```powershell
dotnet test
```

São 8 testes cobrindo a fila local, que é onde mora a lógica de resiliência. Eles usam arquivos
temporários e **não precisam do banco nem da API no ar**.

Para ver que os testes realmente pegam regressão, quebre algo de propósito. Em
[FileSampleQueue.cs](src/MiniMonitor.Agent/Queueing/FileSampleQueue.cs), no método
`TrimIfNeededAsync`, troque `lines.Skip(dropped)` por `lines.Take(_maxItems)`, o que faria a
fila cheia descartar as amostras novas em vez das antigas. Rode `dotnet test` e o teste
`Fila_cheia_descarta_as_amostras_mais_antigas` falha. Desfaça a mudança depois.

---

## Configuração

Tudo tem valor padrão, então o projeto roda sem configurar nada. Para mudar, dá para editar os
arquivos ou usar variáveis de ambiente, que têm precedência.

### Banco

Copie `.env.example` para `.env` e ajuste. O `docker-compose.yml` lê esse arquivo sozinho.

| Variável | Padrão |
|---|---|
| `POSTGRES_USER` | `minimonitor` |
| `POSTGRES_PASSWORD` | `minimonitor` |
| `POSTGRES_DB` | `minimonitor` |
| `POSTGRES_PORT` | `5432` |

Se mudar as credenciais, ajuste também a connection string da API, pela variável
`ConnectionStrings__Postgres` (o duplo underline representa a hierarquia do JSON).

### Agente

Em [src/MiniMonitor.Agent/appsettings.json](src/MiniMonitor.Agent/appsettings.json), ou por
variável de ambiente no formato `Agent__NomeDaChave`.

| Chave | Padrão | Descrição |
|---|---|---|
| `ApiBaseUrl` | `http://localhost:5080` | endereço da API |
| `IntervalSeconds` | `5` | intervalo entre coletas |
| `HttpTimeoutSeconds` | `10` | tempo limite de cada envio |
| `QueueFilePath` | `queue/pending.jsonl` | arquivo da fila local |
| `MaxQueuedSamples` | `10000` | teto da fila, descarta as mais antigas |
| `DrainBatchSize` | `50` | amostras enviadas por ciclo |

Exemplo, coletando a cada 2 segundos:

```powershell
$env:Agent__IntervalSeconds = "2"
dotnet run --project src/MiniMonitor.Agent
```

---

## Estrutura

```
src/
  MiniMonitor.Contracts/   DTOs compartilhados entre agente e API
  MiniMonitor.Api/         ASP.NET Core, EF Core, migrations
  MiniMonitor.Agent/       console, coleta e fila local
tests/
  MiniMonitor.Tests/       xUnit
```

O `Contracts` existe para que agente e API não tenham cópias separadas do mesmo contrato. Assim
uma mudança de campo quebra a compilação, em vez de quebrar a integração em silêncio.

---

## Recomeçando do zero

```powershell
docker compose down -v                                   # apaga o banco e o volume
Remove-Item src/MiniMonitor.Agent/queue -Recurse -Force   # limpa a fila local
docker compose up -d
```

O `-v` é o que remove o volume. Sem ele, `docker compose down` para os containers mas mantém os
dados.

---

## Alterando o schema do banco

Só é necessário se você for mexer nas entidades. A ferramenta do EF Core está fixada no
manifesto do repositório:

```powershell
dotnet tool restore
dotnet ef migrations add NomeDaMigration --project src/MiniMonitor.Api
```

A API aplica as migrations pendentes ao subir, então não precisa rodar `database update` na mão.
