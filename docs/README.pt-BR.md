# EgoNet Revival

Servidor ASP.NET Core para emular endpoints EgoNet/RaceNet usados por jogos antigos da Codemasters.

Idioma principal: [English](../README.md) | Traduções: [Espanhol](README.es.md)

O projeto nasceu para recuperar os recursos RaceNet do DiRT Showdown no PC, principalmente Challenges, ghosts e conquistas que dependem do serviço descontinuado. A ideia de longo prazo é manter uma base comum para adicionar outros jogos EgoNet/RaceNet depois.

## Aviso Sobre Rankings de Conquistas

Este projeto é voltado para preservação de jogos e para restaurar funcionalidades EgoNet/RaceNet descontinuadas. Ele não é aprovado pela Steam Hunters e não deve ser usado para competir em rankings da Steam Hunters ou para validação de conquistas nessa plataforma. O servidor não desbloqueia conquistas diretamente, não edita saves e não altera estatísticas da Steam; ele apenas restaura o fluxo online que o jogo usava.

## Apoie o Projeto

EgoNet Revival é gratuito e open source. O apoio financeiro é opcional e ajuda a cobrir hospedagem do servidor público, armazenamento, banda, contas de teste e trabalho futuro em outros jogos da Codemasters afetados por serviços EgoNet/RaceNet descontinuados.

Apoiar o projeto não compra conquistas, acesso privado, desbloqueios prioritários ou tratamento especial. O objetivo é preservação de jogos e restauração dos fluxos online normais dentro do jogo.

Link de apoio: [PayPal](https://www.paypal.com/donate/?hosted_button_id=T495EZZJMZHEC)

## Jogos Suportados

| Jogo | Plataforma | Estado |
| --- | --- | --- |
| DiRT Showdown | Steam / PC | Em teste, Challenges parcialmente funcionais |

## DiRT Showdown

Funcionando no servidor atual:

- Fluxo básico EgoNet Login/Tick.
- Tela de Challenges com amigos.
- View Challenges.
- IssueChallenge depois de terminar evento.
- UploadGhost.
- DownloadGhost usando ghost enviado pelo jogo.
- SubmitChallengeResult / SubmitPersonalRecord.
- Persistência SQLite para jogadores, amigos, challenges, ghosts e resultados.
- Mais de um desafio para o mesmo amigo.

Ainda não finalizado:

- Teste público mais amplo com várias contas Steam reais.
- Painel/admin.
- Separação formal por perfis de jogo.

## Estrutura

- `src/RaceNetShowdown.Server`: servidor ASP.NET Core usado hoje pelo DiRT Showdown.
- `src/RaceNetShowdown.Patcher`: ferramenta que troca a CA RaceNet embutida no executável do jogo.
- `src/RaceNetShowdown.TlsProbe`: ferramenta de diagnóstico TLS para ver se um jogo chega na porta 443 antes de virar request HTTP.

Os nomes internos ainda carregam `Showdown` porque esse foi o primeiro jogo implementado. A intenção é extrair interfaces compartilhadas e perfis por jogo conforme novos jogos forem adicionados.

## Como Rodar Localmente

1. Feche o DiRT Showdown.
2. Rode `patch-game.cmd` como Administrador.
3. Abra `EgoNetRevival.sln` no Visual Studio.
4. Selecione o projeto `RaceNetShowdown.Server`.
5. Rode com o perfil `RaceNetShowdown.Server`.
6. Abra o jogo e entre nas telas RaceNet.

O servidor escuta por padrão:

- `http://127.0.0.1:80`
- `https://127.0.0.1:443`

O jogo precisa resolver `prod.egonet.codemasters.com` para a máquina que está rodando o servidor.

## Logs e Armazenamento

Por padrão o projeto não grava payloads de requests/responses em disco nem no banco.

Configurações relevantes em `src/RaceNetShowdown.Server/appsettings.json`:

```json
"CaptureRequests": false,
"RecordCalls": false
```

Use `CaptureRequests = true` apenas para engenharia reversa/debug local. Isso cria arquivos em `src/RaceNetShowdown.Server/logs`, que não devem ser enviados para o Git.

Use `RecordCalls = true` apenas se quiser guardar histórico de chamadas no banco durante diagnóstico.

## Vários Desafios Para o Mesmo Amigo

Antes de tornar o serviço público, valide este fluxo:

1. Inicie o servidor.
2. Com a conta A, termine um evento e envie um challenge para a conta B.
3. Sem reiniciar o servidor, termine outro evento e envie outro challenge para a mesma conta B.
4. Entre com a conta B e abra `Challenges > View Challenges` nesse amigo.
5. A tela deve listar mais de um challenge para o mesmo jogador.

## Hospedagem com Docker

O caminho recomendado para teste público é uma VPS pequena com Docker, como um Droplet Basic da DigitalOcean.

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

E persiste o estado de jogo em SQLite:

```txt
data/db/egonet-revival.db
```

Não envie essas pastas para o Git. `data/certs` precisa continuar estável no servidor porque todos os jogadores precisam patchar o jogo com a mesma CA gerada por esse deploy.

Configuração principal do compose:

- `ListenAnyIp = true`
- portas `80` e `443` expostas
- `StoreProvider = Sqlite`
- `CaptureRequests = false`
- `RecordCalls = false`

O modo público guarda jogadores, amigos observados, challenges enviados, ghost cars enviados e resultados de challenges no SQLite. O histórico de requests/responses continua desativado, exceto se `RecordCalls` for habilitado manualmente.

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

Secrets necessários no GitHub:

- `DROPLET_HOST`: IP público do Droplet.
- `DROPLET_USER`: usuário SSH, normalmente `root` no primeiro setup.
- `DROPLET_SSH_KEY`: chave privada SSH usada pelo GitHub Actions.
- `DROPLET_PORT`: opcional, usa `22` se ficar vazio.
- `DEPLOY_PATH`: opcional, usa `/opt/egonet-revival` se ficar vazio.

O job de deploy usa o ambiente `production`. Se quiser aprovação manual antes do deploy em produção, configure required reviewers nesse environment no GitHub.

## Proteção da Branch Main

Depois do primeiro workflow aparecer no GitHub:

1. Vá em `Settings > Branches`.
2. Crie uma branch protection rule para `main`.
3. Marque `Require a pull request before merging`.
4. Marque `Require approvals` e coloque `1`.
5. Marque `Require status checks to pass before merging`.
6. Selecione o check `Build`.
7. Marque `Require conversation resolution before merging`.
8. Marque `Do not allow bypassing the above settings` se quiser que até admin siga a regra.

Com isso, mudanças futuras devem entrar por PR, passar no GitHub Actions e receber sua aprovação antes do merge na `main`.

## Jogadores Externos

Fluxo recomendado para jogadores:

1. Baixar `install-dirt-showdown-mod.cmd`.
2. Fechar o DiRT Showdown.
3. Rodar `install-dirt-showdown-mod.cmd` como Administrador.
4. Abrir o DiRT Showdown e entrar no RaceNet.

O instalador é um único arquivo de comando do Windows. Ele atualiza o arquivo `hosts`, baixa e instala a CA raiz do servidor, patcha `showdown.exe` e `showdown_avx.exe`, limpa o DNS e valida o endpoint HTTPS de saúde.

Fluxo manual:

Exemplo de entradas no `hosts`:

```txt
142.93.206.37 prod.egonet.codemasters.com
142.93.206.37 egonet.codemasters.com
142.93.206.37 racenet.codemasters.com
142.93.206.37 api.racenet.codemasters.com
142.93.206.37 showdown.racenet.codemasters.com
142.93.206.37 racenet.com
142.93.206.37 www.racenet.com
142.93.206.37 api.racenet.com
```

Rode o script de patch a partir da pasta do repositório:

```powershell
cd "C:\Users\dyego\Desktop\Dirt Showdown"
.\patch-game-from-server.cmd
```

Quando o script pedir, informe:

```txt
142.93.206.37
```

O script baixa a CA em:

```txt
http://IP_DO_SERVIDOR/racenet-root-ca.cer
```

Depois ele patcha `showdown.exe` e `showdown_avx.exe` com essa CA.

## Scripts

- `install-dirt-showdown-mod.cmd`: instalador autocontido para jogadores usarem o serviço hospedado do DiRT Showdown.
- `patch-game.cmd`: gera/verifica a CA local e patcha o DiRT Showdown para usar o servidor local.
- `patch-game-from-server.cmd`: baixa a CA do servidor hospedado e patcha o jogo para esse servidor.
- `restore-game-patch.cmd`: restaura os executáveis originais a partir dos backups.
- `status-game-patch.cmd`: mostra se os executáveis estão patchados.
- `regenerate-certs.cmd`: gera/verifica certificados sem iniciar o servidor.
- `probe-tls.cmd`: roda o diagnóstico TLS.

## TLS Probe

`RaceNetShowdown.TlsProbe` é uma ferramenta de diagnóstico. Ela não faz parte do caminho normal do servidor.

Ela escuta nas portas `80` e `443` e imprime os primeiros bytes enviados pelo jogo, especialmente o `TLS ClientHello`. Isso ajuda quando o jogo falha antes da request chegar no ASP.NET. Ela mostra:

- se o jogo está chegando no IP esperado;
- se a conexão chega em `127.0.0.1` ou outro endereço;
- versão TLS anunciada;
- cipher suites;
- se existe SNI;
- se a falha acontece antes ou depois do handshake HTTPS.

Você não precisa do TLS Probe para uso normal. Ele fica no repositório porque deve ser útil para investigar outros jogos EgoNet/RaceNet.
