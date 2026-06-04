# Zenith Audio Player

Zenith Audio Player es un reproductor de musica para Windows creado con C#, WinUI 3, XAML y Windows App SDK. El enfoque del proyecto es una mesa de escucha local para usuarios audiofilos: biblioteca propia, FLAC/WAV/Hi-Res PCM, DSD/DSF/DFF, SACD ISO, cadena de senal visible, medidor VU, caratulas, letras y un asistente opcional llamado ZenithAI.

![Zenith Audio Player](https://github.com/user-attachments/assets/48a8c3c3-3bad-4e4a-a698-935777a93ffb)

**Version actual:** `1.0.7`  
**Instalador recomendado:** [`ZenithAudio_v1.0.7_Setup_win-x64.exe`](https://github.com/xn1llox/Zenith-Audio-Player/releases/download/v1.0.7/ZenithAudio_v1.0.7_Setup_win-x64.exe)

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
- Log local de diagnostico de ZenithAI sin guardar claves:
  `%LOCALAPPDATA%\ZenithAudio\zenithai.log`

## Caracteristicas

- Interfaz moderna con WinUI 3 para Windows 10 y Windows 11.
- Dashboard audiofilo con vista de escucha, laboratorio Anti-Fake y letras.
- Cadena de senal con estado de archivo, decoder, DSP y salida.
- LED de estado bit-perfect.
- Medidor VU digital azul estilo equipo hi-fi.
- Visualizador de audio de fondo.
- Caratulas desde metadata embebida o archivos `cover.jpg`, `cover.png`, etc.
- Letras sincronizadas `.lrc` y letras simples `.txt`.
- Escaneo de biblioteca local con buscador.
- Doble click en biblioteca para reproducir.
- Controles de reproduccion con anterior, siguiente, pausa, stop, aleatorio, volumen y barra de tiempo.
- Selector de dispositivos de salida detectados por Windows.
- Modo compartido y modo exclusivo segun disponibilidad del dispositivo.
- Fallback de reproduccion cuando no existen DLL nativas de BASS/MPV.
- Conversion DSD a PCM en RAM cuando no hay DAC DSD o backend nativo disponible.
- Extraccion SACD ISO mediante `sacd_extract.exe`.
- ZenithAI con configuracion de API desde la app.
- Instalador x64 con Inno Setup.

## ZenithAI

ZenithAI es un asistente de audio integrado. Puede responder sobre:

- historia musical
- artistas y albumes
- diferencias entre DSD, FLAC, WAV, PCM y formatos con perdida
- configuracion de Windows Audio
- WASAPI, BASS, MPV y rutas de reproduccion
- escucha critica
- recomendaciones de perfil tonal

ZenithAI usa una API compatible con OpenAI Chat Completions. La configuracion se guarda por usuario en Windows. No se suben claves al repositorio.

### Comandos locales de tono

Algunos comandos no esperan respuesta de la nube. Zenith Audio los detecta y aplica el perfil directamente sobre el control de tono local:

| Comando | Resultado |
| --- | --- |
| `quiero audio mas puro` | Deja DSP en bypass y salida plana |
| `quiero mas graves` | Sube subgraves con preamp seguro |
| `quiero menos graves` | Reduce subgraves |
| `quiero mas agudos` | Sube presencia y aire |
| `quiero menos agudos` | Reduce brillo y sibilancia |
| `quiero audio mas calido` | Da mas cuerpo y suaviza aire |
| `quiero mas detalle` | Realza presencia y aire |
| `quiero voces al frente` | Enfoca la zona vocal |
| `ajusta el perfil automatico segun la musica` | Elige perfil segun formato/pista |

Estos ajustes modifican:

- `EQ`
- `Omitir DSP`
- `Preamp`
- `Subgraves`
- `Presencia`
- `Aire`

Para evitar clipping digital, los perfiles que suben graves o agudos aplican preamp negativo.

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
| DSD DSF | `.dsf` | Soportado; fallback a PCM si no hay DAC/backend DSD |
| DSD DFF | `.dff` | Soportado; fallback PCM para DFF/DSDIFF sin compresion DST |
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
- Visual Studio 2022 recomendado para WinUI 3.
- Windows 10 build 19041+ o Windows 11.
- Inno Setup 6 para compilar el instalador.

## Compilar

```powershell
dotnet restore .\ZenithAudio.sln
dotnet build .\src\ZenithAudio\ZenithAudio.csproj -c Release -r win-x64
dotnet run --project .\src\ZenithAudio\ZenithAudio.csproj -c Release
```

## Crear instalador

El script del instalador esta en `installer/ZenithAudio.iss`.

```powershell
.\scripts\Prepare-InstallerRedist.ps1
dotnet publish .\src\ZenithAudio\ZenithAudio.csproj -c Release -r win-x64 --self-contained false
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' .\installer\ZenithAudio.iss
```

El instalador generado queda en:

```text
artifacts/installer/
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
- PayPal: `felipeespinozaguajardo@gmail.com`

## Licencia

Este proyecto esta licenciado bajo Apache License 2.0. Revisa [LICENSE](LICENSE).
