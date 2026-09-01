# EgoNet Revival

Servidor ASP.NET Core open-source para reemplazar servicios EgoNet/RaceNet descontinuados de Codemasters.

EgoNet Revival actualmente se centra en restaurar el flujo de Challenges de RaceNet en DiRT Showdown para Steam/PC. El objetivo a largo plazo es mantener una base común para soportar otros juegos de Codemasters afectados por servicios EgoNet/RaceNet descontinuados.

Idioma principal: [English](../README.md) | Traducción: [Portugués (Brasil)](README.pt-BR.md)

## Estado Actual

| Juego | Plataforma | Estado |
| --- | --- | --- |
| DiRT Showdown | Steam / PC | En prueba pública, flujo de Challenges funcional |
| GRID 2 | Steam / PC | Solo discovery/prototipo local, todavía no listo para jugadores |

## Paquetes y Releases por Juego

Este repositorio es una base compartida para varios proyectos de restauración EgoNet/RaceNet. Cada juego tiene su propia carpeta de paquete, instalador, notas de release y prefijo de tag.

| Juego | Paquete | Prefijo de tag | Instalador |
| --- | --- | --- | --- |
| DiRT Showdown | [`games/dirt-showdown`](../games/dirt-showdown) | `dirt-showdown-v` | `EgoNet Revival - DiRT Showdown Installer.exe` |
| GRID 2 | Sin paquete todavía | No disponible todavía | No disponible todavía |

Las releases están separadas por juego. Ejemplo:

```sh
git tag dirt-showdown-v0.1.0
git push origin dirt-showdown-v0.1.0
```

Esto publica el instalador visual de DiRT Showdown, el instalador `.cmd` alternativo, checksums, README y notas de release sin mezclarlos con paquetes de otros juegos en el futuro.

Los scripts auxiliares de desarrollo están agrupados por juego en [`tools`](../tools). Los assets públicos para jugadores permanecen en [`games`](../games) y en GitHub Releases.

Funciona actualmente en DiRT Showdown:

- Login y tick de sesión EgoNet/RaceNet.
- Vista general de amigos y Challenges.
- View Challenges.
- IssueChallenge después de terminar un evento.
- AcceptChallenge.
- UploadGhost y DownloadGhost.
- SubmitChallengeResult / SubmitPersonalRecord.
- Persistencia SQLite para jugadores, amigos, challenges, ghosts y resultados.
- Más de un desafío para el mismo amigo.
- El tally de challenges dominados sigue guardado después de superar un challenge.

Todavía en mejora:

- Pruebas públicas más amplias con varias cuentas reales de Steam.
- Panel/admin.
- Perfiles compartidos más limpios para juegos futuros.
- Discovery local y captura de requests de GRID 2.

## Instalar el Mod de DiRT Showdown

Este es el flujo recomendado para jugadores normales que solo quieren usar el servidor público hospedado.

Requisitos:

- Windows.
- Versión Steam de DiRT Showdown instalada.
- Acceso de Administrador en el PC.
- El juego debe estar cerrado antes de instalar.

Pasos:

1. Abre la [página de releases de DiRT Showdown](https://github.com/Berleis/egonet-revival/releases?q=dirt-showdown-v&expanded=true).
2. Descarga `EgoNet Revival - DiRT Showdown Installer.exe` desde la release `dirt-showdown-v...` más reciente.
3. Haz clic derecho en el instalador.
4. Haz clic en `Ejecutar como administrador`.
5. Elige la carpeta de instalación de DiRT Showdown si no se detecta automáticamente.
6. Acepta el permiso de Windows.
7. Haz clic en `Install Mod`.
8. Abre DiRT Showdown normalmente desde Steam.
9. Entra en RaceNet / Challenges dentro del juego.

La release también incluye `install-dirt-showdown-mod.cmd` como alternativa por línea de comandos. Builds de desarrollo de ese script están en [`games/dirt-showdown/install-dirt-showdown-mod.cmd`](../games/dirt-showdown/install-dirt-showdown-mod.cmd). Los scripts auxiliares para desarrolladores están en [`tools/dirt-showdown`](../tools/dirt-showdown).

Si usas la alternativa `.cmd` y DiRT Showdown no está instalado en la carpeta predeterminada de Steam, ejecútala desde Command Prompt o PowerShell indicando la ruta personalizada:

```powershell
.\install-dirt-showdown-mod.cmd 142.93.206.37 "D:\SteamLibrary\steamapps\common\DiRT Showdown"
```

El instalador es autocontenido. Hace lo siguiente:

- apunta los hostnames RaceNet/EgoNet al servidor hospedado;
- descarga e instala el certificado raíz del servidor;
- parchea `showdown.exe` y `showdown_avx.exe` con ese certificado;
- crea backups `.racenet-original.bak` antes de cambiar ejecutables;
- limpia la caché DNS de Windows;
- prueba el endpoint HTTPS de salud.

Para deshacer el parche de los ejecutables, usa la opción `Verificar integridad de los archivos del juego` en Steam para DiRT Showdown. Si también quieres eliminar por completo la redirección, borra el bloque `EgoNet Revival DiRT Showdown` del archivo `hosts` de Windows.

## Cómo Funciona

DiRT Showdown todavía intenta comunicarse con los endpoints originales de RaceNet/EgoNet, pero esos servicios ya no están disponibles. EgoNet Revival recrea las partes de ese servicio que el juego necesita para Challenges.

El instalador redirige los hostnames RaceNet del juego al servidor sustituto e instala una autoridad certificadora local en la que el ejecutable del juego pasa a confiar después del parche. Después de eso, el juego puede volver a hacer sus requests HTTPS normales.

El servidor recibe los payloads binarios EgoNet originales del juego, lee la función de servicio solicitada y devuelve respuestas compatibles. Para DiRT Showdown, guarda perfiles de jugadores, amigos observados, challenges enviados, uploads de ghost, downloads de ghost y resultados de challenges en SQLite.

Esto no es un desbloqueador de logros, editor de partidas guardadas ni editor de estadísticas de Steam. Los logros siguen siendo activados por el propio juego cuando se completa el flujo restaurado dentro del juego.

## Aviso Sobre Rankings de Logros

Este proyecto está orientado a la preservación de juegos y a restaurar funcionalidades EgoNet/RaceNet descontinuadas. No está aprobado por Steam Hunters y no debe usarse para competir en rankings de Steam Hunters ni para validación de logros en esa plataforma. El servidor no desbloquea logros directamente, no edita partidas guardadas y no cambia estadísticas de Steam; solo restaura el flujo online que usaba el juego.

## Apoya el Proyecto

EgoNet Revival es gratuito y open source. El apoyo financiero es opcional y ayuda a cubrir el hosting del servidor público, almacenamiento, ancho de banda, cuentas de prueba y trabajo futuro en otros juegos de Codemasters afectados por servicios EgoNet/RaceNet descontinuados.

Apoyar el proyecto no compra logros, acceso privado, desbloqueos prioritarios ni trato especial. El objetivo es la preservación de juegos y la restauración de los flujos online normales dentro del juego.

Link de apoyo: [PayPal](https://www.paypal.com/donate/?hosted_button_id=T495EZZJMZHEC)

## Entorno de Desarrollo

Requisitos:

- .NET SDK compatible con el target framework de la solution.
- Visual Studio o `dotnet` CLI.
- Acceso de Administrador si se van a usar directamente los puertos `80` y `443`.
- DiRT Showdown instalado localmente para pruebas de punta a punta.

Flujo local:

1. Cierra DiRT Showdown.
2. Ejecuta `tools\dirt-showdown\patch-local.cmd` como Administrador.
3. Abre `EgoNetRevival.sln` en Visual Studio.
4. Selecciona el proyecto `RaceNetShowdown.Server`.
5. Ejecuta el perfil `RaceNetShowdown.Server`.
6. Abre el juego y entra en las pantallas RaceNet.

El servidor local escucha en:

- `http://127.0.0.1:80`
- `https://127.0.0.1:443`

Por defecto, la captura de payloads de requests/responses está desactivada:

```json
"CaptureRequests": false,
"RecordCalls": false
```

Usa estas opciones solo para ingeniería inversa local o diagnóstico. Payloads capturados y certificados locales no deben subirse al Git.

GRID 2 está solo en fase de discovery. Los scripts en [`tools/grid-2`](../tools/grid-2) pueden preparar una instalación local de GRID 2 y ejecutar el servidor con captura de requests, pero todavía no hay release pública ni instalador para jugadores.

## Hosting Público

El servidor público puede correr en una VPS pequeña con Docker.

```sh
git clone https://github.com/Berleis/egonet-revival.git
cd egonet-revival
docker compose up -d --build
```

El `docker-compose.yml` expone:

- `80:80`
- `443:443`

Datos persistentes de runtime:

- `data/certs`: certificados raíz/servidor generados.
- `data/db/egonet-revival.db`: estado de juego en SQLite.

No subas estas carpetas al Git. La carpeta `data/certs` debe permanecer estable en un servidor público porque los jugadores parchean el juego con el certificado raíz generado por ese deploy.

## Estructura del Repositorio

- `games/games.json`: manifiesto de los paquetes de juegos soportados.
- `games/dirt-showdown`: paquete, proyecto del instalador visual, instalador `.cmd` alternativo y notas de release de DiRT Showdown.
- `.github/workflows/game-releases.yml`: empaqueta assets de release por juego a partir de tags específicas.
- `scripts/package-game-release.ps1`: empaqueta un juego soportado para artifacts de CI/release.
- `src/RaceNetShowdown.Server`: servidor ASP.NET Core usado actualmente por DiRT Showdown.
- `src/RaceNetShowdown.Patcher`: herramienta de parche usada por los scripts locales de desarrollo.
- `src/RaceNetShowdown.TlsProbe`: herramienta de diagnóstico TLS para investigar conexiones iniciales.
- `tools/dirt-showdown`: scripts de desarrollo para parche local, parche contra servidor hospedado, restauración, status, regeneración de certificados y diagnóstico TLS de DiRT Showdown.
- `tools/grid-2`: scripts iniciales para parche local, servidor de discovery y diagnóstico TLS de GRID 2.

Los nombres internos todavía incluyen `Showdown` porque DiRT Showdown es el primer juego implementado. La intención es extraer interfaces compartidas y perfiles por juego a medida que se agreguen más juegos.

## TLS Probe

`RaceNetShowdown.TlsProbe` es solo una herramienta de diagnóstico. No forma parte del camino normal del servidor y no es necesaria para jugadores.

Escucha en los puertos `80` y `443` e imprime los primeros bytes enviados por el juego, especialmente el `TLS ClientHello`. Esto ayuda a diagnosticar fallos que ocurren antes de que una request llegue a ASP.NET.
