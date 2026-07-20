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
