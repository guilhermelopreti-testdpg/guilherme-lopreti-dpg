# Decisões

Anotações do que decidi ao longo do desafio e por quê, escritas conforme desenvolvo.

## Organização do repositório

Repositório único com uma solution, em vez de separar agente e API. Os dois precisam concordar
sobre o formato do JSON trocado entre eles, e separados eu teria que publicar esse contrato num
feed NuGet só para o agente consumir. Num repositório só, o contrato vira um projeto
compartilhado e o compilador reclama se eu mudar um campo de um lado e esquecer do outro.

A divisão em `src/` e `tests/` segue a convenção dos repositórios oficiais do .NET. Não apliquei
arquitetura formal, o critério foi um projeto por executável, mais o de contratos que os dois
referenciam.

## Sinal coletado

Título da janela ativa em vez da lista de processos. Gera um registro por amostra, o que deixa a
agregação mais direta, e depende de API específica do sistema operacional, então me obriga a
isolar essa parte atrás de uma interface.

## Banco de dados

EF Core com Npgsql em vez de SQL na mão. Pesou ter as migrations versionadas junto do código,
assim quem clonar cria o schema do zero com um comando. Na query do relatório vou garantir que a
agregação aconteça no banco, não em memória.

## Timestamps

O contrato usa `DateTimeOffset` e não `DateTime` para o horário da coleta. `DateTime` guarda a
data e a hora mas não guarda o fuso, guarda só uma flag `Kind` dizendo se é UTC ou local, e essa
flag não sobrevive de forma confiável à ida e volta em JSON. O valor chega certo mas o
significado se perde. `DateTimeOffset` serializa o deslocamento junto, como
`2026-07-20T14:30:00+00:00`, então não sobra ambiguidade para a API interpretar.

Tem um detalhe do Npgsql que reforça a escolha, da versão 6 em diante ele lança exceção se você
tentar gravar num `timestamptz` um `DateTime` que não esteja marcado como UTC. Usando
`DateTimeOffset` sempre com offset zero, isso não acontece.

Separei também o horário da coleta do horário de recebimento. O primeiro é carimbado pelo
agente, porque é quando o evento aconteceu de fato, e o segundo pela API. Os dois precisam
existir porque o relógio da máquina do agente pode estar errado, e porque com a fila local uma
amostra pode ser coletada bem antes de conseguir ser entregue.

Na ingestão a API ainda chama `ToUniversalTime()` no horário recebido, em vez de confiar que o
cliente mandou em UTC. O agente manda certo, mas uma chamada feita pelo Swagger ou pelo curl
pode vir com fuso local, e aí o Npgsql recusaria gravar. Testei mandando
`2026-07-20T12:30:00-03:00` e o registro gravado ficou `2026-07-20T15:30:00+00:00`, mesmo
instante, normalizado.

## Contratos separados da entidade

O projeto de contratos tem um record de request e outro de response, e nenhum dos dois é a
entidade que o EF Core vai mapear. A diferença prática são os campos que o servidor controla, o
agente não manda `Id` nem `ReceivedAtUtc`. Além disso, mudança interna no banco não deveria
vazar automaticamente para o formato que o agente consome.

Usei `record` em vez de `class` porque DTO é um pacote de dados, não tem comportamento. Record
já vem com igualdade por valor, que vai simplificar os testes da fila, e com as propriedades
imutáveis depois de criadas.

## Agente

Usei o template de worker service em vez de console puro. Os dois geram um executável de
console, mas o worker já vem com o Generic Host, que traz injeção de dependência, a mesma
cascata de configuração da API e logging estruturado. Também trata `Ctrl+C` cancelando o
`CancellationToken`, então o agente encerra sem cortar um envio pela metade.

A coleta fica atrás da interface `IActivityCollector`, e ela devolve só o título da janela.
Hostname, usuário e horário ficam de fora de propósito, porque o .NET resolve os três igual em
qualquer plataforma. Deixando só o que é realmente específico de sistema operacional, portar
para outra plataforma vira escrever um método, não uma classe inteira.

A escolha da implementação acontece no `Program.cs`, não dentro do worker. O worker só conhece
a interface. Se o agente for iniciado fora do Windows ele para na hora com uma mensagem
explicando o motivo, o que prefiro a falhar silenciosamente mais tarde.

O laço usa `PeriodicTimer` em vez de `Task.Delay`. Com `Task.Delay` o intervalo real vira cinco
segundos mais o tempo da coleta e do envio, então o ritmo escorrega ao longo da execução. O
`PeriodicTimer` dispara em intervalo fixo.

O cliente HTTP é registrado com `AddHttpClient` em vez de instanciado na mão. Criar um
`HttpClient` novo a cada envio deixaria conexões em `TIME_WAIT` e acabaria esgotando as portas
do sistema, que é um problema que só aparece depois de horas rodando.

O `try/catch` fica dentro do laço, não em volta dele. É isso que faz a API fora do ar não
derrubar o agente. Nesta etapa a amostra daquele momento ainda é perdida, o que a fila local
resolve depois.

## Coleta em Linux e macOS

Não implementei, mas o desenho já está preparado, seria uma nova classe implementando
`IActivityCollector` e uma linha diferente no `Program.cs`.

No **Linux** depende do servidor gráfico. No X11 dá para ler a propriedade `_NET_ACTIVE_WINDOW`
da janela raiz para descobrir a janela em foco e depois `_NET_WM_NAME` para o título, seja
chamando `libX11` por P/Invoke, seja executando `xprop` e lendo a saída. No Wayland isso não
existe de forma geral, e não é omissão, é decisão de segurança do protocolo, uma aplicação não
enxerga as janelas das outras. Cada compositor resolve do seu jeito, no GNOME seria uma
extensão do Shell exposta por D-Bus, no KDE seria script do KWin. Ou seja, no Wayland a resposta
honesta é que não tem solução portável, e eu documentaria a limitação em vez de fingir que tem.

No **macOS** o caminho é o `NSWorkspace`, cuja propriedade `frontmostApplication` dá o
aplicativo em primeiro plano com facilidade. Pegar o **título da janela** é mais chato, exige
`CGWindowListCopyWindowInfo` ou a API de acessibilidade, e desde o Catalina ler título de janela
de outro aplicativo depende de permissão concedida pelo usuário nas preferências do sistema.
Então lá o agente teria que lidar com o caso de a permissão não ter sido dada, provavelmente
caindo para só o nome do aplicativo.

## API

Usei Minimal APIs em vez de Controllers. Para três endpoints, fica mais direto e simples, e dá para ler a API inteira de uma vez. Para não cair no problema clássico
de inchar o `Program.cs`, os endpoints ficam num método de extensão em arquivo separado, e o
`Program.cs` só chama ele. Se a API crescesse muito eu migraria para Controllers, o que não é
muito complexo porque a lógica de dentro do handler é a mesma.

A leitura tem paginação com teto de 200 itens. Sem isso o endpoint funcionaria bem em
desenvolvimento e derrubaria a aplicação quando a tabela crescesse.

As migrations são aplicadas no startup da API. Isso deixa o projeto rodar com um comando só
depois de subir o Postgres, que é o que quero para quem for avaliar. Em produção seria errado,
duas instâncias subindo juntas competiriam pelo schema e ninguém revisaria a alteração antes
dela acontecer, então lá isso viraria um passo separado do deploy.

Deixei a API em HTTP puro, sem HTTPS. Como tudo roda local, o certificado autoassinado de
desenvolvimento só criaria atrito, o agente recusaria a conexão por certificado não confiável e
seria preciso rodar `dotnet dev-certs https --trust` antes. A porta está fixada em 5080 no
`launchSettings.json`, que por isso vai versionado, senão o README apontaria para uma porta
sorteada na criação do projeto.

## Postgres no Docker

Volume nomeado em vez de bind mount, porque com bind mount os arquivos internos do banco cairiam
dentro do repositório e ainda haveria problema de permissão no Windows, já que o Postgres roda
como outro usuário dentro do container.

Coloquei healthcheck porque o container aparece como `Up` alguns segundos antes do banco aceitar
conexão, e nessa janela a API tomaria erro ao subir. Isso resolve a inicialização, mas não o
banco cair depois. O passo seguinte seria retry na API, que ainda não fiz.

## Configuração e segredos

Credenciais vêm de variáveis de ambiente, com valores padrão no `docker-compose.yml`. O projeto
sobe com um `docker compose up` puro, sem arquivo extra, e ainda dá para trocar por credenciais
reais sem tocar em nada versionado. O `.env.example` documenta as variáveis, o `.env` está no
`.gitignore`. Em produção isso viria de um gerenciador de segredos.
