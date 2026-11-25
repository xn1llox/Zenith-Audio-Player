# Zenith Audio Player

Zenith Audio Player es un reproductor de musica audiófilo para Windows creado con C#, WinUI 3, XAML y Windows App SDK. Esta disenado como una mesa de escucha local de alta fidelidad: biblioteca masiva (FLAC/WAV/Hi-Res PCM, DSD/DSF/DFF, SACD ISO, CUE), salida **WASAPI** y **ASIO Nativo DSD64/DSD128** estilo foobar2000, cadena de senal visible, medidor VU, ecualizador biquad, caratulas, letras sincronizadas, asistente integrado **ZenithAI** multi-proveedor y **Control Remoto Web por red local (Wi-Fi / LAN)** con Tailwind CSS.

<img width="1919" height="1026" alt="image" src="https://github.com/user-attachments/assets/18d6842e-4b97-4ef6-a507-4d05c42dac64" />


**Version actual:** `1.0.8`
**Instalador recomendado:** [`ZenithAudio_v1.0.8_Setup_win-x64.exe`](https://github.com/xn1llox/Zenith-Audio-Player/releases/download/v1.0.8/ZenithAudio_v1.0.8_Setup_win-x64.exe)

## Novedades de 1.0.8

- Motor de audio estilo foobar2000 con seleccion rapida de dispositivo de salida y cambio en caliente.
- Nuevo motor **ASIO Nativo (DSD64 / DSD128)** compatible con DACs USB ASIO (incluyendo FiiO / JadeAudio JA11) y selector de driver + panel de control ASIO.
- Decodificador DSD/DSF/DFF reconstruido con filtro FIR de fase lineal Blackman-Harris, ganancia SACD seleccionable (+0/+3/+6 dB), frecuencias PCM objetivo y DoP Exclusivo con proteccion de seguridad.
- Modo Exclusivo desactivado por defecto con deteccion de controlador DAC USB dedicado y alerta protectora para evitar ruido blanco en salidas integradas (jack 3.5 mm / headsets).
- Ecualizador biquad real de 3 bandas + preamp con bypass bit-transparente.
- Correccion de carga de carpetas de musica grandes (60 GB+) con tolerancia a subcarpetas protegidas, botones rapidos `+ Carpeta` / `Musica Win` y persistencia automatica.
- Interfaz OLED redisenada con barra superior oscura de Windows 11, iconos vectoriales Hi-Fi de reproduccion, pestanas derechas (`Senal`, `Tono / EQ`, `Ajustes`) y diseno completamente responsivo.
- **ZenithAI** integrado directamente con el reproductor (mini-reproductor en vivo, control de reproduccion/biblioteca/DAC/ASIO/DSD/EQ) y nuevo menu de 3 vistas (`Chat`, `Acciones`, `Config API`) con 11 proveedores preconfigurados y extraccion universal de modelos (`/v1/models`, `/models`, Ollama `/api/tags`).
- **Control Remoto Web por red local (Wi-Fi / LAN)** con boton `Remoto` junto a `ZenithAI` para iniciar/detener el servidor y pagina responsiva con Tailwind CSS para controlar pistas, volumen, salida, motor, DSD y EQ desde el telefono.
- **Actualizador automatico desde GitHub Releases** integrado en `Ajustes` (comprobacion al iniciar, barra de progreso e instalacion silenciosa con reapertura automatica).
- **Pipeline de audio optimizado estilo foobar2000:** buffer de pre-lectura en RAM (~550 ms) con latencia de hardware WASAPI reducida (45–90 ms), lector DSD secuencial de 512 KB, *seek* sin chasquidos electricos (silencio DSD `0x69` + rampa cosenoidal *de-click*) y deslizador de tiempo sin microcortes.

## Novedades de 1.0.7

- ZenithAI reconstruido como panel flotante dentro de la ventana principal.
- Chat movible arrastrando el encabezado.
- Scroll con rueda del mouse dentro del chat.
- Boton `Cancelar` para detener consultas largas.
- Progreso visible durante consultas a NVIDIA NIM.
- Respuestas mas largas para analisis tecnico y musical.
- Aviso cuando la API corta una respuesta por limite de tokens.
- Boton `Comandos` con accesos rapidos para perfiles de tono.
- Comandos locales de ZenithAI para ajustar el sonido sin usar la nube:
  - audio mas puro
  - mas graves
  - menos graves
  - mas agudos
  - menos agudos
  - mas calido
  - mas detalle
  - voces al frente
  - perfil automatico
- Normalizacion de API key cuando se pega con prefijo `Bearer`.
- Modelo NVIDIA NIM por defecto actualizado a `nvidia/nemotron-3-super-120b-a12b`.
- Soporte de payload Nemotron 3 con `reasoning_budget` y `enable_thinking`.
- Log local de diagnostico de ZenithAI sin guardar claves:
  `%LOCALAPPDATA%\ZenithAudio\zenithai.log`

## Caracteristicas

- Interfaz OLED moderna y responsiva con WinUI 3, barra de titulo oscura en Windows 11 e iconos vectoriales Hi-Fi de reproduccion.
- Dashboard audiofilo con vista de escucha, laboratorio Anti-Fake, letras y panel derecho con pestanas (`Senal`, `Tono / EQ`, `Ajustes`).
- Cadena de senal con estado de archivo, decoder, DSP, salida y LED de estado bit-perfect.
- Medidor VU digital azul estilo equipo hi-fi y visualizador de audio en vivo.
- Caratulas desde metadata embebida o archivos `cover.jpg`, `cover.png`, `folder.jpg`, etc., con acento dinamico.
- Letras sincronizadas `.lrc` y letras simples `.txt`.
- Escaneo rapido de bibliotecas grandes (60 GB+) con tolerancia a subcarpetas protegidas, botones `+ Carpeta` / `Musica Win`, buscador y persistencia de carpetas.
- Selector rapido de dispositivo de salida y motor en la barra superior con cambio en caliente estilo foobar2000.
- Motor **Zenith WASAPI** (Compartido y Exclusivo Event/Push con formato `WAVE_FORMAT_EXTENSIBLE` PCM) y motor **ASIO Nativo (DSD64 / DSD128 y PCM)** con selector de driver y acceso directo al panel de control ASIO.
- Proteccion de Modo Exclusivo (desactivado por defecto; detecta drivers DAC USB dedicados y alerta si la salida no cuenta con controlador DAC para evitar ruido blanco en jacks 3.5 mm).
- Decodificador DSD/DSF/DFF con filtro FIR de fase lineal Blackman-Harris, ganancia SACD (+0/+3/+6 dB), frecuencias PCM configurables y DoP Exclusivo.
- Ecualizador biquad real de 3 bandas + preamp con bypass bit-transparente y perfiles AutoEQ.
- Extraccion SACD ISO mediante `sacd_extract.exe` y soporte de hojas `.cue`.
- **ZenithAI** integrado directamente con el reproductor, con menu `Chat / Acciones / Config API`, 11 presets de proveedores y extraccion universal de modelos.
- **Control Remoto Web (Wi-Fi / LAN)** con boton `Remoto` junto a `ZenithAI` e interfaz movil/desktop construida con Tailwind CSS.
- Instalador completo x64 con Inno Setup (incluye `.NET Desktop Runtime 8` y `Windows App Runtime 1.8`).

## ZenithAI

ZenithAI es un asistente audiófilo integrado directamente con Zenith Audio. Cuenta con un panel flotante movible dividido en 3 vistas (**Chat**, **Acciones** y **Config API**) y un **mini-reproductor sincronizado en vivo**.

### Proveedores y extraccion universal de modelos

En la vista **Config API** puedes usar cualquier servidor compatible con OpenAI Chat Completions sin importar el proveedor:

- **11 presets incluidos:** `NVIDIA NIM`, `OpenAI`, `OpenRouter`, `Groq`, `Google Gemini (OpenAI compat)`, `DeepSeek`, `Mistral AI`, `Together AI`, `Ollama (Local)`, `LM Studio (Local)` y `Personalizado`.
- **Extraccion automatica de modelos (`Extraer modelos`):** consulta `/v1/models`, `/models` y `/api/tags` (Ollama), muestra una lista filtrable con buscador instantaneo y permite elegir cualquier modelo con un clic o escribirlo manualmente.
- **Servidores locales sin API key:** `Ollama` y `LM Studio` en `localhost` funcionan sin clave API.
- **Boton `Probar`:** verifica la conexion y respuesta real del modelo elegido antes de volver al chat.
- La configuracion y las claves se guardan cifradas por usuario en Windows (`%LOCALAPPDATA%\ZenithAudio\`) y nunca se suben al repositorio.

Default de NVIDIA NIM:

- Endpoint: `https://integrate.api.nvidia.com/v1/chat/completions`
- Modelo: `nvidia/nemotron-3-super-120b-a12b`
- Parametros Nemotron 3: `max_tokens=8192`, `reasoning_budget=4096`, `enable_thinking=true`

### Integracion directa con el reproductor y comandos locales

ZenithAI puede controlar el reproductor tanto mediante **comandos locales instantaneos** (sin latencia ni consumo de API) como desde **cualquier modelo LLM** mediante acciones estructuradas (`play`, `pause`, `next`, `previous`, `volume`, `seek`, `shuffle`, buscar y reproducir pistas de la biblioteca cargada, cambiar salida de audio, cambiar entre `WASAPI`/`ASIO`/`BASS`/`MPV`, ajustar DSD, aplicar EQ biquad, filtrar biblioteca o lanzar Anti-Fake):

| Comando local | Resultado |
| --- | --- |
| `pon <cancion o album>` | Busca y reproduce la pista en tu biblioteca cargada |
| `pausa` / `reanuda` / `detener` / `siguiente` / `anterior` | Control directo del transporte |
| `volumen 65` | Ajusta el volumen exacto |
| `cambia a asio` / `cambia a wasapi` | Cambia el motor de reproduccion en caliente |
| `filtra dsd` / `filtra hires` | Filtra el explorador de biblioteca |
| `analiza` / `anti-fake` | Ejecuta el analisis espectral Anti-Fake |
| `quiero audio mas puro` | Deja DSP en bypass bit-transparente y salida plana |
| `quiero mas graves` / `menos graves` | Ajusta subgraves con preamp anti-clipping |
| `quiero mas agudos` / `menos agudos` | Ajusta presencia y aire |
| `quiero audio mas calido` / `mas detalle` / `voces al frente` | Aplica perfiles tonales inmediatos |
| `ajusta el perfil automatico segun la musica` | Elige perfil segun formato/pista |

## Control Remoto Web (Wi-Fi / LAN)

Zenith Audio incluye un servidor HTTP embebido para controlar el reproductor de forma inalambrica desde tu telefono, tablet u otro navegador en la misma red local Wi-Fi:

- **Boton `Remoto`** ubicado junto a `ZenithAI` en la barra superior con indicador de estado en vivo.
- Permite **iniciar y detener el servidor** en un clic sin requerir permisos de administrador en Windows (`TcpListener` en puerto configurable, por defecto `8787`).
- Detecta automaticamente tu direccion IP local (`http://192.168.x.x:8787`), permite copiar la URL, abrir `localhost`, configurar **PIN opcional** y activar **inicio automatico**.
- Interfaz web responsiva construida con **Tailwind CSS** (`src/ZenithAudio/Assets/Remote/index.html`) replicando la paleta OLED oscura + cian de Zenith, compatible con *"Anadir a pantalla de inicio"* (PWA):
  - **Ahora:** carátula en vivo con brillo dinamico, barra VU de 20 segmentos, linea de tiempo deslizable, transporte completo, letra sincronizada y volumen (`Mute`, `-5%`, `+5%`, slider).
  - **Canciones:** buscador en vivo sobre toda tu biblioteca cargada con filtros `Todas`, `DSD` y `Hi-Res PCM`.
  - **Audio:** cambio en caliente de dispositivo de salida, motor (`WASAPI`, `ASIO DSD`, `BASS`, `MPV`), driver ASIO, Modo Exclusivo (con proteccion DAC), configuracion DSD y ecualizador biquad + presets.

> **Nota de red:** la primera vez que inicies el servidor remoto, permite el acceso de `ZenithAudio` en el Firewall de Windows para **Redes privadas** si vas a conectarte desde tu telefono.

## Requisitos para ejecutar

### Minimos

- Windows 10 version 2004 / build 19041 o superior, 64 bits.
- CPU x64 de 2 nucleos.
- 4 GB de RAM.
- 500 MB libres para la app instalada.
- 1 GB libre adicional durante la instalacion.
- Dispositivo de audio compatible con Windows Audio / WASAPI.
- Internet solo para ZenithAI o descarga manual de dependencias.

El instalador publicado incluye .NET Desktop Runtime 8 y Windows App Runtime 1.8.

### Recomendados

- Windows 11 64 bits actualizado o Windows 10 22H2 64 bits.
- CPU x64 de 4 nucleos o superior.
- 8 GB de RAM o mas.
- SSD para bibliotecas grandes.
- DAC USB, interfaz de audio o salida WASAPI estable para escucha dedicada.
- 2 GB libres para instalacion, caches temporales y futuras actualizaciones.
- Conexion a internet para ZenithAI mediante API compatible.

## Formatos disponibles

| Formato | Extensiones | Estado |
| --- | --- | --- |
| FLAC | `.flac` | Soportado |
| WAV / PCM | `.wav` | Soportado |
| AIFF | `.aiff`, `.aif` | Soportado |
| ALAC / Apple Lossless | `.alac`, `.m4a` | Soportado segun codecs disponibles |
| MP3 | `.mp3` | Soportado |
| AAC / MP4 audio | `.aac`, `.m4a` | Soportado segun codecs disponibles |
| Ogg / Opus | `.ogg`, `.opus` | Soportado segun Windows Media Foundation o BASS |
| APE / Monkey's Audio | `.ape` | Soportado con BASS y `bass_ape.dll` |
| WavPack | `.wv` | Soportado con BASS y `bass_wv.dll` |
| DSD DSF | `.dsf` | Soportado: ASIO DSD64/DSD128 nativo, DoP Exclusivo o decodificacion FIR Blackman-Harris a PCM |
| DSD DFF | `.dff` | Soportado: ASIO DSD nativo, DoP o FIR PCM para DFF/DSDIFF sin compresion DST |
| SACD ISO | `.iso` | Soportado mediante extraccion a DSF con `sacd_extract.exe` |
| Hojas CUE | `.cue` | Soportado como indice virtual de pistas |
| MQA | `.mqa`, `.flac` con MQA | Reproducible como PCM; decodificacion completa depende del DAC/backend |
| Letras sincronizadas | `.lrc` | Soportado |
| Letras simples | `.txt` | Soportado |
| Caratulas | `.jpg`, `.jpeg`, `.png`, metadata embebida | Soportado |

Notas:

- En equipos sin DAC DSD, Zenith Audio puede convertir DSD temporalmente a PCM para reproducir por Windows/Realtek/USB.
- Los formatos disponibles pueden variar segun el backend activo y los codecs del sistema.
- MPV y BASS son backends opcionales/experimentales. Sus binarios nativos no se distribuyen dentro del repositorio.
- FLAC/WAV multicanal pueden abrirse; en modo compartido Windows puede mezclar o bajar a estereo segun el dispositivo.

## Instalador y actualizaciones

El instalador oficial se publica en GitHub Releases como `.exe` para Windows x64.

Desde las versiones recientes, el instalador:

- Detecta si Zenith Audio ya esta instalado.
- Muestra version instalada y version del instalador.
- Actualiza archivos en el mismo directorio.
- Elimina archivos antiguos del directorio instalado antes de copiar los nuevos.
- Mantiene configuracion local del usuario fuera del directorio de instalacion.
- Instala o repara .NET Desktop Runtime 8 y Windows App Runtime 1.8 cuando corresponde.

La configuracion local, caches temporales y datos generados por usuario se guardan en rutas de usuario de Windows, no dentro de `Program Files`.

## Requisitos para desarrollo

- .NET 8 SDK.
- Visual Studio 2022 o VS Code con Windows App SDK 1.8.
- Windows 10 build 19041+ o Windows 11.
- Inno Setup 6 o Inno Setup 7 para compilar el instalador.

## Compilar

```powershell
dotnet restore .\ZenithAudio.sln
dotnet build .\src\ZenithAudio\ZenithAudio.csproj -c Release -r win-x64
dotnet run --project .\src\ZenithAudio\ZenithAudio.csproj -c Release -r win-x64
```

## Crear instalador

El script del instalador esta en `installer/ZenithAudio.iss`.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Prepare-InstallerRedist.ps1
dotnet build .\src\ZenithAudio\ZenithAudio.csproj -c Release -r win-x64
& 'C:\Program Files\Inno Setup 7\ISCC.exe' .\installer\ZenithAudio.iss
```

El instalador generado queda en:

```text
artifacts/installer/ZenithAudio_v1.0.8_Setup_win-x64.exe
```

## Herramientas nativas opcionales

Los binarios nativos no se suben al repositorio. Para pruebas locales, colocalos en:

```text
src/ZenithAudio/runtimes/win-x64/native/
```

Archivos opcionales soportados:

- `sacd_extract.exe` para extraer SACD ISO a DSF.
- `mpv-2.dll` para pruebas con backend MPV.
- `bass.dll`, `basswasapi.dll`, `bassdsd.dll`, `bass_ape.dll`, `bass_wv.dll` y `bassopus.dll` para pruebas con backend BASS.

## Manual de usuario

- [Manual de usuario en Markdown](docs/MANUAL_DE_USUARIO.md)
- [Manual de usuario en PDF](docs/ZenithAudio_Manual_de_Usuario.pdf)

## Donaciones

Si quieres colaborar con el proyecto y ayudar a seguir sacando actualizaciones:

- BTC: `bc1qqqwtvasyk2j0jdja6fyhkwg84qm53uwz4935d2`
- ETH: `0x0Ce533373C02D5069f193AF0a6e325bdAC8e8F4D`
- LTC: `ltc1qmac3zrd49n552c5xjpwm5n2p0d5kyydy5z6fah`
- Ko-Fi: `https://ko-fi.com/xn1llox`

## Licencia

Desarrollado por **xn1llox** — [www.espinozainformatica.cl](https://www.espinozainformatica.cl)

Este proyecto esta licenciado bajo Apache License 2.0. Revisa [LICENSE](LICENSE).
