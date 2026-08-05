# EgoNet Revival

Servidor ASP.NET Core para emular endpoints EgoNet/RaceNet usados por jogos antigos da Codemasters.

Idioma principal: [English](../README.md) | Traducciones: [Español](README.es.md)

O projeto nasceu para recuperar os recursos RaceNet do DiRT Showdown no PC, principalmente Challenges, ghosts e conquistas que dependem do servico descontinuado. A ideia de longo prazo e manter uma base comum para adicionar outros jogos EgoNet/RaceNet depois.

## Jogos Suportados

| Jogo | Plataforma | Estado |
| --- | --- | --- |
| DiRT Showdown | Steam / PC | Em teste, Challenges parcialmente funcionais |

## DiRT Showdown

Funcionando no servidor atual:

- Fluxo basico EgoNet Login/Tick.
- Tela de Challenges com amigos.
- View Challenges.
- IssueChallenge depois de terminar evento.
- UploadGhost.
- DownloadGhost usando ghost enviado pelo jogo.
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
- `src/RaceNetShowdown.TlsProbe`: ferramenta de diagnostico TLS para ver se um jogo chega na porta 443 antes de virar request HTTP.

Os nomes internos ainda carregam `Showdown` porque esse foi o primeiro jogo implementado. A intencao e extrair interfaces compartilhadas e perfis por jogo conforme novos jogos forem adicionados.

## Como Rodar Localmente

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

## Logs E Armazenamento

Por padrao o projeto nao grava payloads de requests/responses em disco nem no banco.

Configuracoes relevantes em `src/RaceNetShowdown.Server/appsettings.json`:

```json
"CaptureRequests": false,
"RecordCalls": false
```

Use `CaptureRequests = true` apenas para engenharia reversa/debug local. Isso cria arquivos em `src/RaceNetShowdown.Server/logs`, que nao devem ser enviados para o Git.

Use `RecordCalls = true` apenas se quiser guardar historico de chamadas no banco durante diagnostico.

## Varios Desafios Para O Mesmo Amigo

Antes de tornar o servico publico, valide este fluxo:

1. Inicie o servidor.
2. Com a conta A, termine um evento e envie um challenge para a conta B.
3. Sem reiniciar o servidor, termine outro evento e envie outro challenge para a mesma conta B.
4. Entre com a conta B e abra `Challenges > View Challenges` nesse amigo.
5. A tela deve listar mais de um challenge para o mesmo jogador.

## Hospedagem Com Docker

O caminho recomendado para teste publico e uma VPS pequena com Docker, como um Droplet Basic da DigitalOcean.

No servidor:

```sh
git clone https://github.com/Berleis/egonet-revival.git
cd egonet-revival
docker compose up -d --build
```

O `docker-compose.yml` publica:

- `80:80`
- `443:443`

E persiste certificados em:

```txt
data/certs
```

Nao envie essa pasta para o Git. Ela precisa continuar estavel no servidor porque todos os jogadores precisam patchar o jogo com a mesma CA gerada por esse deploy.

Configuracao principal do compose:

- `ListenAnyIp = true`
- portas `80` e `443` expostas
- `CaptureRequests = false`
- `RecordCalls = false`

O modo publico ainda precisa de persistencia real para challenges e ghosts antes de ser considerado pronto para uso continuo. Por enquanto, o estado fica em memoria e se perde quando o container reinicia.

## GitHub Actions

O workflow `.github/workflows/ci.yml` roda em:

- pull requests para `main`;
- pushes na `main`.

Em pull requests, ele executa:

- restore da solution;
- build em Release;
- build da imagem Docker.

Em pushes na `main`, depois do build passar, ele faz deploy no Droplet via SSH e executa:

```sh
git fetch origin main
git reset --hard origin/main
docker compose up -d --build
docker compose ps
```

Secrets necessarios no GitHub:

- `DROPLET_HOST`: IP publico do Droplet.
- `DROPLET_USER`: usuario SSH, normalmente `root` no primeiro setup.
- `DROPLET_SSH_KEY`: chave privada SSH usada pelo GitHub Actions.
- `DROPLET_PORT`: opcional, usa `22` se ficar vazio.
- `DEPLOY_PATH`: opcional, usa `/opt/egonet-revival` se ficar vazio.

O job de deploy usa o ambiente `production`. Se quiser aprovacao manual antes do deploy em producao, configure required reviewers nesse environment no GitHub.

## Protecao Da Branch Main

Depois do primeiro workflow aparecer no GitHub:

1. Va em `Settings > Branches`.
2. Crie uma branch protection rule para `main`.
3. Marque `Require a pull request before merging`.
4. Marque `Require approvals` e coloque `1`.
5. Marque `Require status checks to pass before merging`.
6. Selecione o check `Build`.
7. Marque `Require conversation resolution before merging`.
8. Marque `Do not allow bypassing the above settings` se quiser que ate admin siga a regra.

Com isso, mudancas futuras devem entrar por PR, passar no GitHub Actions e receber sua aprovacao antes do merge na `main`.

## Jogadores Externos

Cada jogador precisa:

1. Apontar `prod.egonet.codemasters.com` para o IP do servidor no arquivo `hosts`.
2. Rodar `patch-game-from-server.cmd`.
3. Informar o IP/host do servidor quando o script pedir.

O script baixa a CA em:

```txt
http://IP_DO_SERVIDOR/racenet-root-ca.cer
```

Depois ele patcha `showdown.exe` e `showdown_avx.exe` com essa CA.

## Scripts

- `patch-game.cmd`: gera/verifica a CA local e patcha o DiRT Showdown para usar o servidor local.
- `patch-game-from-server.cmd`: baixa a CA do servidor hospedado e patcha o jogo para esse servidor.
- `restore-game-patch.cmd`: restaura os executaveis originais a partir dos backups.
- `status-game-patch.cmd`: mostra se os executaveis estao patchados.
- `regenerate-certs.cmd`: gera/verifica certificados sem iniciar o servidor.
- `probe-tls.cmd`: roda o diagnostico TLS.

## TLS Probe

`RaceNetShowdown.TlsProbe` e uma ferramenta de diagnostico. Ela nao faz parte do caminho normal do servidor.

Ela escuta nas portas `80` e `443` e imprime os primeiros bytes enviados pelo jogo, especialmente o `TLS ClientHello`. Isso ajuda quando o jogo falha antes da request chegar no ASP.NET. Ela mostra:

- se o jogo esta chegando no IP esperado;
- se a conexao chega em `127.0.0.1` ou outro endereco;
- versao TLS anunciada;
- cipher suites;
- se existe SNI;
- se a falha acontece antes ou depois do handshake HTTPS.

Voce nao precisa do TLS Probe para uso normal. Ele fica no repositorio porque deve ser util para investigar outros jogos EgoNet/RaceNet.
