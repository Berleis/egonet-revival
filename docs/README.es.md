# EgoNet Revival

Servidor ASP.NET Core para emular endpoints EgoNet/RaceNet usados por juegos antiguos de Codemasters.

Idioma principal: [English](../README.md) | Traduccion: [Portugues (Brasil)](README.pt-BR.md)

El proyecto nacio para recuperar funciones de RaceNet en DiRT Showdown para PC, especialmente Challenges, ghosts y logros que dependen del servicio descontinuado. El objetivo a largo plazo es mantener una base comun para agregar otros juegos EgoNet/RaceNet mas adelante.

## Juegos Soportados

| Juego | Plataforma | Estado |
| --- | --- | --- |
| DiRT Showdown | Steam / PC | En pruebas, Challenges parcialmente funcionales |

## DiRT Showdown

Funciona actualmente:

- Flujo basico EgoNet Login/Tick.
- Pantalla de Challenges con amigos.
- View Challenges.
- IssueChallenge despues de terminar un evento.
- UploadGhost.
- DownloadGhost usando ghost enviado por el juego.
- SubmitChallengeResult / SubmitPersonalRecord.
- Persistencia SQLite para jugadores, amigos, challenges, ghosts y resultados.
- Mas de un desafio para el mismo amigo.

Todavia no esta terminado:

- Pruebas publicas mas amplias con varias cuentas reales de Steam.
- Panel/admin.
- Separacion formal por perfiles de juego.

## Estructura

- `src/RaceNetShowdown.Server`: servidor ASP.NET Core usado actualmente por DiRT Showdown.
- `src/RaceNetShowdown.Patcher`: herramienta que reemplaza la CA RaceNet embebida en el ejecutable del juego.
- `src/RaceNetShowdown.TlsProbe`: herramienta de diagnostico TLS para ver si un juego llega al puerto 443 antes de convertirse en request HTTP.

Los nombres internos todavia incluyen `Showdown` porque DiRT Showdown fue el primer juego implementado. La intencion es extraer interfaces compartidas y perfiles por juego a medida que se agreguen mas juegos.

## Ejecucion Local

1. Cierra DiRT Showdown.
2. Ejecuta `patch-game.cmd` como Administrador.
3. Abre `EgoNetRevival.sln` en Visual Studio.
4. Selecciona el proyecto `RaceNetShowdown.Server`.
5. Ejecuta el perfil `RaceNetShowdown.Server`.
6. Abre el juego y entra en las pantallas de RaceNet.

El servidor escucha por defecto en:

- `http://127.0.0.1:80`
- `https://127.0.0.1:443`

El juego debe resolver `prod.egonet.codemasters.com` hacia la maquina que ejecuta el servidor.

## Logs Y Almacenamiento

Por defecto, el proyecto no guarda payloads de requests/responses en disco ni en la base de datos.

Configuraciones relevantes en `src/RaceNetShowdown.Server/appsettings.json`:

```json
"CaptureRequests": false,
"RecordCalls": false
```

Usa `CaptureRequests = true` solo para ingenieria inversa/debug local. Esto crea archivos en `src/RaceNetShowdown.Server/logs`, que no deben subirse al Git.

Usa `RecordCalls = true` solo si quieres guardar historial de llamadas en la base de datos para diagnostico.

## Varios Desafios Para El Mismo Amigo

Antes de publicar el servicio, valida este flujo:

1. Inicia el servidor.
2. Con la cuenta A, termina un evento y envia un challenge a la cuenta B.
3. Sin reiniciar el servidor, termina otro evento y envia otro challenge a la misma cuenta B.
4. Entra con la cuenta B y abre `Challenges > View Challenges` para ese amigo.
5. La pantalla deberia listar mas de un challenge para el mismo jugador.

## Hosting Con Docker

El camino recomendado para pruebas publicas es una VPS pequena con Docker, como un Droplet Basic de DigitalOcean.

En el servidor:

```sh
git clone https://github.com/Berleis/egonet-revival.git
cd egonet-revival
docker compose up -d --build
```

`docker-compose.yml` publica:

- `80:80`
- `443:443`

Y persiste certificados en:

```txt
data/certs
```

Y persiste el estado de juego en SQLite:

```txt
data/db/egonet-revival.db
```

No subas estas carpetas al Git. `data/certs` debe permanecer estable en el servidor porque todos los jugadores necesitan parchear el juego con la misma CA generada por ese despliegue.

Configuracion principal del compose:

- `ListenAnyIp = true`
- puertos `80` y `443` expuestos
- `StoreProvider = Sqlite`
- `CaptureRequests = false`
- `RecordCalls = false`

El modo publico guarda jugadores, amigos observados, challenges enviados, ghost cars subidos y resultados de challenges en SQLite. El historial de requests/responses sigue desactivado, salvo que `RecordCalls` se active manualmente.

## GitHub Actions

El workflow `.github/workflows/ci.yml` corre en:

- pull requests hacia `main`;
- pushes a `main`.

En pull requests ejecuta:

- restore de la solution;
- build en Release;
- build de la imagen Docker.

En pushes a `main`, despues de pasar el build, despliega al Droplet via SSH y ejecuta:

```sh
git fetch origin main
git reset --hard origin/main
docker compose up -d --build
docker compose ps
```

Secrets necesarios en GitHub:

- `DROPLET_HOST`: IP publica del Droplet.
- `DROPLET_USER`: usuario SSH, normalmente `root` en el primer setup.
- `DROPLET_SSH_KEY`: clave privada SSH usada por GitHub Actions.
- `DROPLET_PORT`: opcional, usa `22` si queda vacio.
- `DEPLOY_PATH`: opcional, usa `/opt/egonet-revival` si queda vacio.

El job de deploy usa el environment `production`. Si tambien quieres aprobacion manual antes del deploy a produccion, configura required reviewers en ese environment en GitHub.

## Proteccion De La Branch Main

Despues de que el primer workflow aparezca en GitHub:

1. Ve a `Settings > Branches`.
2. Crea una branch protection rule para `main`.
3. Activa `Require a pull request before merging`.
4. Activa `Require approvals` y pon `1`.
5. Activa `Require status checks to pass before merging`.
6. Selecciona el check `Build`.
7. Activa `Require conversation resolution before merging`.
8. Activa `Do not allow bypassing the above settings` si quieres que hasta admins sigan la regla.

Con esto, los cambios futuros deben entrar por PR, pasar GitHub Actions y recibir tu aprobacion antes del merge a `main`.

## Jugadores Externos

Flujo recomendado para jugadores:

1. Descargar `install-dirt-showdown-mod.cmd`.
2. Cerrar DiRT Showdown.
3. Ejecutar `install-dirt-showdown-mod.cmd` como Administrador.
4. Abrir DiRT Showdown y entrar en RaceNet.

El instalador es un unico archivo de comando de Windows. Actualiza el archivo `hosts`, descarga e instala la CA raiz del servidor, parchea `showdown.exe` y `showdown_avx.exe`, limpia el DNS y valida el endpoint HTTPS de salud.

Flujo manual:

Ejemplo de entradas en `hosts`:

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

Ejecuta el script de patch desde la carpeta del repositorio:

```powershell
cd "C:\Users\dyego\Desktop\Dirt Showdown"
.\patch-game-from-server.cmd
```

Cuando el script lo pida, informa:

```txt
142.93.206.37
```

El script descarga la CA desde:

```txt
http://IP_DEL_SERVIDOR/racenet-root-ca.cer
```

Luego parchea `showdown.exe` y `showdown_avx.exe` con esa CA.

## Scripts

- `install-dirt-showdown-mod.cmd`: instalador autocontenido para que jugadores usen el servicio hospedado de DiRT Showdown.
- `patch-game.cmd`: genera/verifica la CA local y parchea DiRT Showdown para usar el servidor local.
- `patch-game-from-server.cmd`: descarga la CA del servidor hospedado y parchea el juego para ese servidor.
- `restore-game-patch.cmd`: restaura los ejecutables originales desde backups.
- `status-game-patch.cmd`: muestra si los ejecutables estan parcheados.
- `regenerate-certs.cmd`: genera/verifica certificados sin iniciar el servidor.
- `probe-tls.cmd`: ejecuta el diagnostico TLS.

## TLS Probe

`RaceNetShowdown.TlsProbe` es una herramienta de diagnostico. No forma parte del camino normal del servidor.

Escucha en los puertos `80` y `443` e imprime los primeros bytes enviados por el juego, especialmente el `TLS ClientHello`. Esto ayuda cuando el juego falla antes de que la request llegue a ASP.NET. Puede mostrar:

- si el juego llega a la IP esperada;
- si la conexion llega a `127.0.0.1` u otra direccion;
- version TLS anunciada;
- cipher suites;
- si existe SNI;
- si el fallo ocurre antes o despues del handshake HTTPS.

No necesitas TLS Probe para uso normal. Queda en el repositorio porque deberia ser util para investigar otros juegos EgoNet/RaceNet.
