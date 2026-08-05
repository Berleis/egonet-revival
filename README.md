# EgoNet Revival

Servidor ASP.NET Core para emular endpoints EgoNet/RaceNet usados por jogos antigos da Codemasters.

O projeto nasceu para recuperar os recursos RaceNet do DiRT Showdown no PC, principalmente Challenges, ghosts e conquistas que dependem de servidor. A ideia e manter uma base comum para, depois, adicionar outros jogos que usam protocolos parecidos.

## Jogos suportados

| Jogo | Plataforma | Estado |
| --- | --- | --- |
| DiRT Showdown | Steam / PC | Em teste, Challenges parcialmente funcionais |

## DiRT Showdown

Funcionando no servidor atual:

- Login/Tick basicos do EgoNet.
- Tela de Challenges com amigos.
- View Challenges.
- IssueChallenge depois de terminar evento.
- UploadGhost.
- DownloadGhost usando o ghost enviado pelo jogo.
- SubmitChallengeResult / SubmitPersonalRecord.
- Mais de um desafio para o mesmo amigo na mesma sessao do servidor.

Ainda nao finalizado:

- Persistencia real de challenges/ghosts entre reinicios.
- Banco de dados de producao para varios jogadores.
- Painel/admin.
- Separacao formal por perfis de jogo.

## Estrutura

- `src/RaceNetShowdown.Server`: servidor ASP.NET Core usado hoje pelo DiRT Showdown.
- `src/RaceNetShowdown.Patcher`: ferramenta que troca a CA RaceNet embutida no executavel do jogo.
- `src/RaceNetShowdown.TlsProbe`: ferramenta de diagnostico TLS para descobrir se um jogo chega na porta 443 antes de virar request HTTP.

Os nomes internos ainda carregam `Showdown` porque esse foi o primeiro jogo implementado. A intencao e extrair interfaces/perfis por jogo conforme novos jogos forem adicionados.

## Como rodar localmente

1. Feche o DiRT Showdown.
2. Rode `patch-game.cmd` como Administrador.
3. Abra `EgoNetRevival.sln` no Visual Studio.
4. Selecione o projeto `RaceNetShowdown.Server`.
5. Rode com o perfil `RaceNetShowdown.Server`.
6. Abra o jogo e entre nas telas RaceNet.

O servidor escuta por padrao:

- `http://127.0.0.1:80`
- `https://127.0.0.1:443`

O jogo precisa resolver `prod.egonet.codemasters.com` para a maquina que esta rodando o servidor.

## Logs e armazenamento

Por padrao o projeto nao grava payloads de requests/responses em disco nem no banco.

Configuracoes relevantes em `src/RaceNetShowdown.Server/appsettings.json`:

```json
"CaptureRequests": false,
"RecordCalls": false
```

Use `CaptureRequests = true` apenas para engenharia reversa/debug local. Isso cria arquivos em `src/RaceNetShowdown.Server/logs`, que nao devem ser enviados para o Git.

Use `RecordCalls = true` apenas se quiser guardar historico de chamadas no banco durante diagnostico.

## Teste de varios desafios no mesmo amigo

Para validar antes de publicar:

1. Inicie o servidor.
2. Com uma conta, termine um evento e envie um challenge para o mesmo amigo.
3. Sem reiniciar o servidor, termine outro evento e envie outro challenge para esse mesmo amigo.
4. Entre na outra conta e abra `Challenges > View Challenges` nesse amigo.
5. A tela deve listar mais de um challenge para o mesmo jogador.

## Hospedagem com Docker

O caminho recomendado para teste publico e uma VPS pequena com Docker, como um Droplet Basic.

No servidor:

```sh
git clone https://github.com/SEU_USUARIO/egonet-revival.git
cd egonet-revival
docker compose up -d --build
```

O `docker-compose.yml` publica:

- `80:80`
- `443:443`

E persiste os certificados em:

```txt
data/certs
```

Essa pasta nao deve ir para o Git. Ela precisa continuar existindo no servidor, porque todos os jogadores precisam usar a mesma CA gerada por esse deploy.

Configuracao principal do compose:

- `ListenAnyIp = true`
- portas `80` e `443` liberadas
- `CaptureRequests = false`
- `RecordCalls = false`

O modo publico ainda precisa de persistencia real para challenges e ghosts antes de ser considerado pronto para uso continuo. Por enquanto, o estado fica em memoria e se perde quando o container reinicia.

## Jogadores externos

Cada jogador precisa:

1. Apontar `prod.egonet.codemasters.com` para o IP do servidor no arquivo `hosts`.
2. Rodar `patch-game-from-server.cmd`.
3. Informar o IP/host do servidor quando o script pedir.

O script baixa a CA em:

```txt
http://IP_DO_SERVIDOR/racenet-root-ca.cer
```

E usa essa CA para patchar `showdown.exe` e `showdown_avx.exe`.

## Scripts

- `patch-game.cmd`: gera/verifica a CA local e patcha o DiRT Showdown para confiar no servidor local.
- `patch-game-from-server.cmd`: baixa a CA do servidor hospedado e patcha o jogo para usar esse servidor.
- `restore-game-patch.cmd`: restaura os executaveis originais a partir dos backups.
- `status-game-patch.cmd`: mostra se os executaveis estao patchados.
- `regenerate-certs.cmd`: gera/verifica certificados sem iniciar o servidor.
- `probe-tls.cmd`: roda o diagnostico TLS.

## TLS Probe

`RaceNetShowdown.TlsProbe` e uma ferramenta de diagnostico. Ele nao faz parte do servidor normal.

Ele serve para escutar as portas `80` e `443` e mostrar os primeiros bytes de conexao do jogo, especialmente o `TLS ClientHello`. Isso ajuda quando o jogo mostra erro antes de chegar no ASP.NET. Com ele da para ver:

- se o jogo esta batendo no IP certo;
- se a conexao chega em `127.0.0.1` ou outro IP;
- versao TLS anunciada;
- cipher suites;
- se existe SNI;
- se o problema esta antes ou depois do handshake HTTPS.

Para uso normal, voce nao precisa rodar o TLS Probe. Ele fica no repo porque deve ser util para investigar outros jogos EgoNet/RaceNet no futuro.
