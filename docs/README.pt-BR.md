# EgoNet Revival

Servidor ASP.NET Core open-source para substituir serviços EgoNet/RaceNet descontinuados da Codemasters.

O EgoNet Revival atualmente foca em restaurar o fluxo de Challenges do RaceNet no DiRT Showdown para Steam/PC. A ideia de longo prazo é manter uma base comum para suportar outros jogos da Codemasters afetados por serviços EgoNet/RaceNet descontinuados.

Idioma principal: [English](../README.md) | Tradução: [Espanhol](README.es.md)

## Estado Atual

| Jogo | Plataforma | Estado |
| --- | --- | --- |
| DiRT Showdown | Steam / PC | Em teste público, fluxo de Challenges funcional |
| GRID 2 | Steam / PC | Apenas discovery/protótipo local, ainda não pronto para jogadores |

## Pacotes e Releases por Jogo

Este repositório é uma base compartilhada para vários projetos de restauração EgoNet/RaceNet. Cada jogo tem sua própria pasta de pacote, instalador, notas de release e prefixo de tag.

| Jogo | Pacote | Prefixo de tag | Instalador |
| --- | --- | --- | --- |
| DiRT Showdown | [`games/dirt-showdown`](../games/dirt-showdown) | `dirt-showdown-v` | `EgoNet Revival - DiRT Showdown Installer.exe` |
| GRID 2 | Ainda sem pacote | Ainda indisponível | Ainda indisponível |

As releases são separadas por jogo. Exemplo:

```sh
git tag dirt-showdown-v0.1.0
git push origin dirt-showdown-v0.1.0
```

Isso publica o instalador visual do DiRT Showdown, o instalador `.cmd` alternativo, checksums, README e notas de release sem misturar com pacotes de outros jogos no futuro.

Scripts auxiliares de desenvolvimento ficam agrupados por jogo em [`tools`](../tools). Assets públicos para jogadores ficam em [`games`](../games) e nas GitHub Releases.

Funcionando atualmente no DiRT Showdown:

- Login e tick de sessão EgoNet/RaceNet.
- Visão geral de amigos e Challenges.
- View Challenges.
- IssueChallenge depois de terminar um evento.
- AcceptChallenge.
- UploadGhost e DownloadGhost.
- SubmitChallengeResult / SubmitPersonalRecord.
- Persistência SQLite para jogadores, amigos, challenges, ghosts e resultados.
- Mais de um desafio para o mesmo amigo.
- Tally de challenges dominados continua salvo depois que um challenge é batido.

Ainda em melhoria:

- Teste público mais amplo com várias contas Steam reais.
- Painel/admin.
- Perfis compartilhados mais limpos para jogos futuros.
- Discovery local e captura de requests do GRID 2.

## Instalar o Mod do DiRT Showdown

Este é o fluxo recomendado para jogadores comuns que só querem usar o servidor público hospedado.

Requisitos:

- Windows.
- Versão Steam do DiRT Showdown instalada.
- Acesso de Administrador no PC.
- O jogo precisa estar fechado antes da instalação.

Passos:

1. Abra a [página de releases do DiRT Showdown](https://github.com/Berleis/egonet-revival/releases?q=dirt-showdown-v&expanded=true).
2. Baixe `EgoNet Revival - DiRT Showdown Installer.exe` da release `dirt-showdown-v...` mais recente.
3. Clique com o botão direito no instalador.
4. Clique em `Executar como administrador`.
5. Escolha a pasta de instalação do DiRT Showdown se ela não for detectada automaticamente.
6. Aceite a permissão do Windows.
7. Clique em `Install Mod`.
8. Abra o DiRT Showdown normalmente pela Steam.
9. Entre em RaceNet / Challenges dentro do jogo.

A release também inclui `install-dirt-showdown-mod.cmd` como alternativa por linha de comando. Builds de desenvolvimento desse script ficam em [`games/dirt-showdown/install-dirt-showdown-mod.cmd`](../games/dirt-showdown/install-dirt-showdown-mod.cmd). Scripts auxiliares para desenvolvedores ficam em [`tools/dirt-showdown`](../tools/dirt-showdown).

Se você usar a alternativa `.cmd` e o DiRT Showdown não estiver instalado na pasta padrão da Steam, rode pelo Prompt de Comando ou PowerShell informando o caminho personalizado:

```powershell
.\install-dirt-showdown-mod.cmd 142.93.206.37 "D:\SteamLibrary\steamapps\common\DiRT Showdown"
```

O instalador é autocontido. Ele:

- aponta os hostnames RaceNet/EgoNet para o servidor hospedado;
- baixa e instala o certificado raiz do servidor;
- patcha `showdown.exe` e `showdown_avx.exe` com esse certificado;
- cria backups `.racenet-original.bak` antes de alterar os executáveis;
- limpa o cache DNS do Windows;
- testa o endpoint HTTPS de saúde.

Para desfazer o patch dos executáveis, use a opção `Verificar integridade dos arquivos do jogo` na Steam para o DiRT Showdown. Se também quiser remover completamente o redirecionamento, apague o bloco `EgoNet Revival DiRT Showdown` do arquivo `hosts` do Windows.

## Como Funciona

O DiRT Showdown ainda tenta conversar com os endpoints RaceNet/EgoNet originais, mas esses serviços não estão mais disponíveis. O EgoNet Revival recria as partes desse serviço que o jogo precisa para Challenges.

O instalador redireciona os hostnames RaceNet do jogo para o servidor substituto e instala uma autoridade certificadora local que o executável do jogo passa a confiar depois do patch. Depois disso, o jogo consegue fazer suas requisições HTTPS normais novamente.

O servidor recebe os payloads binários EgoNet originais do jogo, lê a função de serviço solicitada e retorna respostas compatíveis. Para o DiRT Showdown, ele armazena perfis de jogadores, amigos observados, challenges enviados, uploads de ghost, downloads de ghost e resultados de challenges em SQLite.

Isto não é um desbloqueador de conquistas, editor de save ou editor de estatísticas da Steam. As conquistas continuam sendo acionadas pelo próprio jogo quando o fluxo restaurado dentro do jogo é concluído.

## Aviso Sobre Rankings de Conquistas

Este projeto é voltado para preservação de jogos e para restaurar funcionalidades EgoNet/RaceNet descontinuadas. Ele não é aprovado pela Steam Hunters e não deve ser usado para competir em rankings da Steam Hunters ou para validação de conquistas nessa plataforma. O servidor não desbloqueia conquistas diretamente, não edita saves e não altera estatísticas da Steam; ele apenas restaura o fluxo online que o jogo usava.

## Apoie o Projeto

EgoNet Revival é gratuito e open source. O apoio financeiro é opcional e ajuda a cobrir hospedagem do servidor público, armazenamento, banda, contas de teste e trabalho futuro em outros jogos da Codemasters afetados por serviços EgoNet/RaceNet descontinuados.

Apoiar o projeto não compra conquistas, acesso privado, desbloqueios prioritários ou tratamento especial. O objetivo é preservação de jogos e restauração dos fluxos online normais dentro do jogo.

Link de apoio: [PayPal](https://www.paypal.com/donate/?hosted_button_id=T495EZZJMZHEC)

## Ambiente de Desenvolvimento

Requisitos:

- .NET SDK compatível com o target framework da solution.
- Visual Studio ou `dotnet` CLI.
- Acesso de Administrador se for usar diretamente as portas `80` e `443`.
- DiRT Showdown instalado localmente para testes de ponta a ponta.

Fluxo local:

1. Feche o DiRT Showdown.
2. Rode `tools\dirt-showdown\patch-local.cmd` como Administrador.
3. Abra `EgoNetRevival.sln` no Visual Studio.
4. Selecione o projeto `RaceNetShowdown.Server`.
5. Rode com o perfil `RaceNetShowdown.Server`.
6. Abra o jogo e entre nas telas RaceNet.

O servidor local escuta em:

- `http://127.0.0.1:80`
- `https://127.0.0.1:443`

Por padrão, a captura de payloads de requests/responses fica desativada:

```json
"CaptureRequests": false,
"RecordCalls": false
```

Use essas opções apenas para engenharia reversa local ou diagnóstico. Payloads capturados e certificados locais não devem ser enviados para o Git.

O GRID 2 está apenas em fase de discovery. Os scripts em [`tools/grid-2`](../tools/grid-2) conseguem preparar uma instalação local do GRID 2 e rodar o servidor com captura de requests, mas ainda não existe release pública nem instalador para jogadores.

## Hospedagem Pública

O servidor público pode rodar em uma VPS pequena com Docker.

```sh
git clone https://github.com/Berleis/egonet-revival.git
cd egonet-revival
docker compose up -d --build
```

O `docker-compose.yml` expõe:

- `80:80`
- `443:443`

Dados persistentes de runtime:

- `data/certs`: certificados raiz/servidor gerados.
- `data/db/egonet-revival.db`: estado de jogo em SQLite.

Não envie essas pastas para o Git. A pasta `data/certs` precisa continuar estável em um servidor público porque os jogadores patcham o jogo com o certificado raiz gerado por esse deploy.

## Estrutura do Repositório

- `games/games.json`: manifesto dos pacotes de jogos suportados.
- `games/dirt-showdown`: pacote, projeto do instalador visual, instalador `.cmd` alternativo e notas de release do DiRT Showdown.
- `.github/workflows/game-releases.yml`: empacota assets de release por jogo a partir de tags específicas.
- `scripts/package-game-release.ps1`: empacota um jogo suportado para artifacts de CI/release.
- `src/RaceNetShowdown.Server`: servidor ASP.NET Core usado atualmente pelo DiRT Showdown.
- `src/RaceNetShowdown.Patcher`: ferramenta de patch usada pelos scripts locais de desenvolvimento.
- `src/RaceNetShowdown.TlsProbe`: ferramenta de diagnóstico TLS para investigar conexões iniciais.
- `tools/dirt-showdown`: scripts de desenvolvimento para patch local, patch contra servidor hospedado, restauração, status, regeneração de certificados e diagnóstico TLS do DiRT Showdown.
- `tools/grid-2`: scripts iniciais para patch local, servidor de discovery e diagnóstico TLS do GRID 2.

Os nomes internos ainda carregam `Showdown` porque DiRT Showdown é o primeiro jogo implementado. A intenção é extrair interfaces compartilhadas e perfis por jogo conforme novos jogos forem adicionados.

## TLS Probe

`RaceNetShowdown.TlsProbe` é apenas uma ferramenta de diagnóstico. Ela não faz parte do caminho normal do servidor e não é necessária para jogadores.

Ela escuta nas portas `80` e `443` e imprime os primeiros bytes enviados pelo jogo, especialmente o `TLS ClientHello`. Isso ajuda a diagnosticar falhas que acontecem antes de uma request chegar ao ASP.NET.
