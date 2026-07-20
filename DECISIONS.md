# Decisões

Anotações do que decidi e por quê, escritas conforme desenvolvi.

## Organização do repositório

Repositório único com uma solution, em vez de separar agente e API. Os dois precisam concordar
sobre o formato do JSON trocado entre eles, e separados eu teria que publicar esse contrato num
feed NuGet só para o agente consumir. Junto, o contrato vira um projeto compartilhado e o
compilador reclama se eu mudar um campo de um lado e esquecer do outro.

A divisão em `src/` e `tests/` segue a convenção dos repositórios oficiais do .NET. Não apliquei
arquitetura formal, o critério foi um projeto por executável, mais o de contratos que os dois
referenciam.

## Sinal coletado

Título da janela ativa em vez da lista de processos. Gera um registro por amostra, o que deixa a
agregação mais direta, e depende de API específica do sistema operacional, então me obriga a
isolar essa parte atrás de uma interface.

## Timestamps

O contrato usa `DateTimeOffset` e não `DateTime`. `DateTime` guarda data e hora mas não guarda o
fuso, guarda só uma flag `Kind`, e essa flag não sobrevive de forma confiável à ida e volta em
JSON, então o valor chega certo mas o significado se perde. `DateTimeOffset` serializa o
deslocamento junto, como `2026-07-20T14:30:00+00:00`, e não sobra ambiguidade. Isso ainda casa
com o Npgsql, que da versão 6 em diante recusa gravar num `timestamptz` um `DateTime` que não
esteja marcado como UTC.

Tudo é gravado em UTC, de propósito. Um registro das 18:30 UTC são 15:30 aqui no Brasil, e é o
mesmo instante. Guardar o horário local de cada máquina quebraria a comparação entre máquinas em
fusos diferentes e criaria registros ambíguos no horário de verão. Converter para o fuso de quem
está lendo é responsabilidade da apresentação, não do armazenamento.

Separei o horário da coleta do horário de recebimento. O primeiro é carimbado pelo agente,
porque é quando o evento aconteceu, o segundo pela API. Os dois precisam existir porque o relógio
da máquina do agente pode estar errado, e porque com a fila local uma amostra pode ser coletada
bem antes de conseguir ser entregue.

Na ingestão a API chama `ToUniversalTime()` em vez de confiar que o cliente mandou em UTC. O
agente manda certo, mas uma chamada pelo Swagger pode vir com fuso local. Testei mandando
`2026-07-20T12:30:00-03:00` e o registro gravado ficou `2026-07-20T15:30:00+00:00`.

## Contratos separados da entidade

O projeto de contratos tem um record de request e outro de response, e nenhum dos dois é a
entidade que o EF Core mapeia. A diferença prática são os campos que o servidor controla, o
agente não manda `Id` nem `ReceivedAtUtc`, e mudança interna no banco não deveria vazar para o
formato que o agente consome.

Usei `record` porque DTO é um pacote de dados, sem comportamento. Vem com igualdade por valor,
que simplificou os testes da fila, e com propriedades imutáveis depois de criadas. A entidade,
ao contrário, é `class` com propriedades graváveis, porque o EF precisa criar o objeto vazio e
preencher.

## Agente

Template de worker service em vez de console puro. Os dois geram um executável de console, mas o
worker já vem com o Generic Host, que traz injeção de dependência, a mesma cascata de
configuração da API e logging estruturado, além de tratar `Ctrl+C` cancelando o
`CancellationToken`.

A coleta fica atrás da interface `IActivityCollector`, que devolve só o título da janela.
Hostname, usuário e horário ficam de fora porque o .NET resolve os três igual em qualquer
plataforma. Deixando só o que é realmente específico de sistema operacional, portar vira
escrever um método, não uma classe inteira. A escolha da implementação acontece no `Program.cs`,
o worker só conhece a interface, e fora do Windows o agente para na inicialização com uma
mensagem explicando o motivo.

O laço usa `PeriodicTimer` e não `Task.Delay`, senão o intervalo real viraria cinco segundos mais
o tempo da coleta e do envio, e o ritmo escorregaria ao longo da execução. O cliente HTTP é
registrado com `AddHttpClient`, porque criar um `HttpClient` a cada envio deixaria conexões em
`TIME_WAIT` até esgotar as portas do sistema.

O `try/catch` fica dentro do laço, não em volta dele. É isso que faz a API fora do ar não
derrubar o agente.

## Fila local do agente

A fila é um arquivo em disco, em JSON Lines, e não uma lista em memória. Memória resolveria a API
cair, mas não o agente ser fechado ou a máquina reiniciar. JSON Lines porque dá para acrescentar
no fim sem reescrever o arquivo, o que um array JSON exigiria a cada amostra.

A amostra é gravada antes de qualquer tentativa de envio, e só sai do arquivo depois da resposta
de sucesso. Isso dá entrega pelo menos uma vez, se o agente morrer entre a resposta e a remoção
da linha, aquela amostra é reenviada e vira duplicata. Remover antes de enviar trocaria duplicata
por perda, e num monitor contagem levemente inflada incomoda menos que buraco no histórico.

O envio para no primeiro erro do lote, confirmando só o que passou, o que preserva a ordem
cronológica. Insistir nas seguintes não faria sentido, se a API caiu ela caiu para todas.

A remoção grava num arquivo temporário e move por cima. Reescrevendo direto, uma queda no meio
deixaria o arquivo pela metade e levaria a fila inteira junto, justamente no momento de falha.

A fila tem teto de dez mil amostras, senão a API fora por dias encheria o disco, o que é pior do
que perder amostra. Ao estourar, descarta as mais antigas, porque num monitor o dado recente vale
mais.

Testei o cenário completo, subi o agente sem a API, deixei acumular, matei o agente, subi a API e
reabri o agente. As represadas chegaram, e dá para ver isso nos próprios dados, elas têm trinta e
cinco segundos entre coleta e recebimento, enquanto as coletadas depois têm quarenta
milissegundos.

## API

Minimal APIs em vez de Controllers. Para três endpoints fica mais direto, e dá para ler a API
inteira de uma vez. Para não inchar o `Program.cs`, os endpoints ficam em métodos de extensão em
arquivos separados. Se crescesse muito eu migraria para Controllers, o que não é traumático
porque a lógica de dentro do handler é a mesma.

A leitura tem paginação com teto de 200 itens, senão o endpoint funcionaria bem em
desenvolvimento e derrubaria a aplicação quando a tabela crescesse.

As migrations são aplicadas no startup, o que deixa o projeto rodar com um comando só depois de
subir o Postgres. Em produção seria errado, duas instâncias subindo juntas competiriam pelo
schema e ninguém revisaria a alteração antes, então lá viraria um passo separado do deploy.

Deixei em HTTP puro. Como tudo roda local, o certificado autoassinado só criaria atrito, o agente
recusaria a conexão e seria preciso rodar `dotnet dev-certs https --trust` antes. A porta está
fixada em 5080 no `launchSettings.json`, que por isso vai versionado, senão o README apontaria
para uma porta sorteada na criação do projeto.

## Relatório

Responde em quais janelas cada máquina passou o tempo num período. Como o agente coleta em
intervalo fixo, contar amostras é uma aproximação de medir tempo, a duração não é gravada em
lugar nenhum, sai da contagem.

A agregação acontece no banco, com `GROUP BY`, `COUNT`, `MIN` e `MAX`. Isso importa porque em
LINQ o jeito certo e o jeito errado são quase idênticos na tela, muda só onde entra o
`ToListAsync`, e materializar cedo demais traria a tabela inteira para a memória só para contar.
Conferi o SQL nos logs para ter certeza de que o `GROUP BY` chegou no Postgres.

O período é semiaberto, inclui o início e exclui o fim, senão uma amostra na virada apareceria em
duas horas seguidas. O total vem de uma consulta separada e não da soma dos itens, porque a lista
é cortada pelo limite. E a resposta devolve o período efetivamente considerado, senão quem chama
sem parâmetros não saberia o que a última hora significou.

O índice ficou só em `captured_at_utc`, único filtro presente em toda consulta do relatório, já
que o hostname é opcional. Um índice composto com hostname na frente seria melhor para as
consultas filtradas por máquina e inútil para as que não filtram, porque o Postgres não faz busca
por faixa eficiente quando a primeira coluna não está restrita. Se a consulta por máquina virasse
o padrão dominante eu acrescentaria o composto, mas índice tem custo, cada um deixa a escrita mais
lenta, e aqui a escrita é constante.

## Testes

Testei a fila local, que é onde mora a lógica autoral e onde um bug seria silencioso. Testar que
o EF grava no banco testaria a Microsoft, não o meu código.

São oito testes cobrindo as regras que, se quebrassem, quebrariam sem avisar: ordem de chegada,
leitura não remover nada sem confirmação, confirmação parcial quando a API aceita parte do lote,
sobreviver ao agente reiniciar, descartar as antigas ao encher, e o horário em UTC sobreviver à
ida e volta do arquivo. Cada teste usa um arquivo temporário próprio, então não dependem de banco
nem de API no ar e não interferem entre si.

Para confirmar que os testes têm valor, inverti de propósito a regra de descarte da fila cheia e
o teste correspondente falhou, como esperado.

## Coleta em Linux e macOS

Não implementei, mas a mudança no projeto seria pequena, porque a única parte que depende do
sistema operacional está atrás da interface `IActivityCollector`. Seria uma classe nova por
plataforma e mais um caso no `if` do `Program.cs`, que é onde a implementação é escolhida.
Nenhum outro arquivo muda.

A parte difícil é o que vai dentro dessas classes, porque cada sistema responde a pergunta "qual
janela está na frente" de um jeito diferente.

No **Linux** quem gerencia as janelas é o servidor gráfico, e existem duas gerações dele. No
X11, mais antigo e ainda bem comum, dá para perguntar qual é a janela ativa, e existe até um
programa de linha de comando pronto para isso, o `xprop`, então a versão mais simples seria
executá-lo e ler a saída. Já o Wayland, mais novo, de propósito não deixa um programa enxergar as
janelas dos outros, por segurança. Ali não existe solução que funcione em qualquer ambiente, cada
desktop resolve do seu jeito, e eu documentaria a limitação em vez de fingir que tem saída.

No **macOS** descobrir qual aplicativo está em primeiro plano é fácil, o sistema expõe isso.
Descobrir o título da janela é mais chato, porque o macOS trata isso como dado sensível e exige
que o usuário autorize nas configurações de privacidade. O agente teria que tratar o caso de a
permissão não ter sido concedida, provavelmente usando só o nome do aplicativo.

Ou seja, as três plataformas não entregam exatamente a mesma informação, o que é um argumento a
favor de a interface devolver algo simples e opcional, como faz hoje.

## Postgres no Docker

Healthcheck porque o container aparece como `Up` alguns segundos antes do banco aceitar conexão,
e nessa janela a API tomaria erro ao subir.

Credenciais vêm de variáveis de ambiente, com valores padrão no próprio compose. O projeto sobe
com um `docker compose up` puro, sem arquivo extra, e ainda dá para trocar por credenciais reais
sem tocar em nada versionado. O `.env.example` documenta as variáveis, o `.env` está no
`.gitignore`. Em produção isso viria de um gerenciador de segredos.

## O que faltou e o que faria com mais tempo

**Push em tempo real.** Era item do Nível 3 e não fiz. Faria com WebSocket, mantendo a conexão
aberta entre a API e um cliente simples, e avisando assim que uma amostra nova fosse gravada, em
vez de o cliente ficar perguntando de tempos em tempos. O que eu pensaria antes de sair
codificando é o que enviar, porque mandar cada amostra para a tela não seria muito útil, faria
mais sentido enviar o resumo já contado.

**Ingestão em lote.** O agente envia uma requisição por amostra, então drenar uma fila grande são
milhares de chamadas. Um endpoint aceitando um array resolveria, mas muda o contrato e o ganho só
aparece depois de uma queda longa.

**Backoff exponencial.** Com a API fora, o agente tenta a cada cinco segundos indefinidamente. Um
backoff com teto pouparia recurso dos dois lados.

**Retry de conexão na API.** O healthcheck resolve a inicialização, mas se o banco cair depois a
API não se recupera sozinha. Faria com a estratégia de resiliência do próprio Npgsql.

**Autenticação.** Hoje qualquer um posta amostra em nome de qualquer máquina. Num cenário real
cada agente teria uma credencial e a API validaria que o hostname enviado bate com quem está
autenticado.

**Retenção de dados.** A tabela cresce para sempre. Faria particionamento por data ou uma rotina
que apaga amostras antigas depois de agregá-las.

## Onde usei IA

Usei o Claude durante todo o desafio, principalmente para acelerar o repetitivo, como montar os
projetos e escrever o P/Invoke da user32, e para discutir alternativas antes de decidir, tipo
Minimal APIs contra Controllers ou volume nomeado contra bind mount. Em cada passo pedi a
explicação do porquê antes do código.

Dois pontos onde precisei corrigir:

O primeiro foi na query do relatório. O código gerado projetava o resultado do `GroupBy` direto
no construtor do record, que é a forma mais natural de escrever e que o EF Core 8 não traduz.
Compilou normalmente e só quebrou quando rodei, com um `InvalidOperationException` em tempo de
execução. Corrigi projetando para tipo anônimo, materializando e só então convertendo para o
record, o que mantém a agregação no banco, e depois fui aos logs conferir o SQL gerado.

O segundo foi mais simples e do mesmo tipo. O comando de instalar o pacote do Npgsql sem fixar
versão trouxe a linha 10, incompatível com o .NET 8 do projeto, e o restore falhou.

O padrão dos dois é o mesmo, a intenção estava certa e o detalhe da versão ou da implementação
estava errado, e os dois só apareceram porque rodei e testei em vez de confiar que estava certo.
Foi o que mais me marcou, com ORM principalmente, código compilar não prova nada.
