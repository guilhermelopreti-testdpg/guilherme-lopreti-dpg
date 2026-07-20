# Desafio Técnico — Desenvolvedor(a) .NET Júnior

Bem-vindo(a)! Este é um desafio prático pra fazer em **~1 dia**. Ele é uma versão
minúscula da arquitetura do nosso produto: um **agente** roda na máquina, coleta
dados e envia pra uma **API central**, que grava num **PostgreSQL**, e um
**relatório** transforma isso em informação.

Não queremos que você acerte tudo. Queremos ver **como você pensa, decide e
comunica**.

## Regras do jogo

- **Pode (e deve) usar o Claude.** É assim que trabalhamos — não é trapaça. Mas
  você tem que **entender e conseguir defender** tudo que entregar.
- **Timebox de ~1 dia.** Entregar **menos, bem feito e
  honesto** vale mais do que entregar tudo pela metade.
- Se não terminar algo, **escreva no `DECISIONS.md` o que faltou e como faria** —
  isso conta ponto, não tira.
- **Stack:** C# / .NET (sugerimos .NET 8) + PostgreSQL. O resto é sua escolha.
- Entregue um **repositório Git** (link) com **histórico de commits** — não um
  único commit gigante no final.

## O que construir: "Mini-Monitor"

Três peças pequenas:

### 1. Agente (console app .NET)
A cada X segundos, coleta **um** sinal do sistema e envia pra API. O sinal deve
conter no mínimo: `hostname`, `usuário`, `timestamp (UTC)` e **um** destes:
- a **lista de processos em execução** (nome), ou
- o **título da janela ativa** (foreground window).

Escolha um. (O título da janela é mais interessante — veja o Nível 3.)

### 2. API (ASP.NET Core)
Recebe o dado e grava no PostgreSQL. Precisa de:
- um endpoint de **ingestão** (o agente chama), e
- um endpoint de **leitura** (retorna o que foi capturado).

### 3. Relatório
Um endpoint (ou query documentada) que responde **uma pergunta de agregação**.
Exemplos: "quantas vezes cada processo apareceu na última hora" ou
"amostras por máquina, por hora".

## Níveis

Faça na ordem. Cada nível assume o anterior.

**Nível 1 — Faça funcionar**
- Agente envia → API grava no Postgres → dá pra ler de volta.
- `README.md` com o passo a passo pra rodar do zero.

**Nível 2 — O que esperamos de um bom júnior**
- **Resiliência:** se a API estiver fora do ar, o agente **não pode travar nem
  perder dado** (fila/retry local, e envia quando a API voltar).
- **Timestamps em UTC, corretos.** (Atenção: isso derruba muita gente.)
- Uma **agregação de verdade** no relatório (não só "devolve tudo").
- Pelo menos **1 teste automatizado** cobrindo alguma lógica sua.

**Nível 3 — Diferencial (mostra seu teto)**
- A coleta do sinal é **específica de sistema operacional**. Abstraia atrás de uma
  interface (ex.: `IActivityCollector`) com implementação **Windows**, e no
  `DECISIONS.md` descreva **como faria em Linux e macOS** (não precisa implementar).
- `docker-compose` subindo o PostgreSQL.
- Push em **tempo real** (SignalR/WebSocket) pra um cliente simples.
- **Índice** no Postgres pensado pra query do relatório.

## Entregáveis

- **Repositório Git** com histórico de commits.
- **`README.md`** — como rodar tudo, do zero, passo a passo.
- **`DECISIONS.md`** — o coração da avaliação. Conte:
  - suas **decisões e trade-offs**;
  - o que faria **com mais tempo**;
  - como levaria a coleta pra **Linux e macOS**;
  - **onde você usou IA** e onde precisou **corrigir ou desconfiar** dela.

## Como avaliamos

- **Roda?** Conseguimos subir seguindo seu README, sem adivinhar nada.
- **Clareza** do código e organização.
- **Resiliência** — API fora não derruba o agente.
- **Correção** — UTC certo, agregação certa.
- **Cross-platform** — pensou na abstração e no plano Linux/Mac?
- **Comunicação escrita** — README e DECISIONS claros (aqui a colaboração é
  assíncrona; escrever bem é parte do trabalho).
- **Uso de IA com julgamento** — acelerou **e** entendeu/verificou, ou colou no
  escuro?
- **Higiene de Git** — commits pequenos e com mensagem que faz sentido.

## Depois

Uma conversa curta (~30–40 min) onde você **mostra o código rodando** e explica
1–2 decisões. É aqui que vemos que é seu de verdade. Pode trazer o que não
terminou — a gente valoriza honestidade.

Boa sorte. Capriche no `DECISIONS.md`. 🙂