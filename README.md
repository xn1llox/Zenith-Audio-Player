# Zenith Audio Player

Zenith Audio Player es un reproductor de música para Windows creado con C#, WinUI 3, XAML y Windows App SDK. El proyecto está enfocado en bibliotecas locales de alta resolución, reproducción DSD/DSF, extracción SACD ISO, configuración de dispositivos de audio, visualización audiófila, letras, carátulas y un asistente opcional llamado ZenithAI.

<img width="1914" height="1025" alt="image" src="https://github.com/user-attachments/assets/a1a53a4f-4c47-4f00-b304-5a64c6cf8455" />

**Versión actual:** `1.0.6`  
**Instalador recomendado:** [`ZenithAudio_v1.0.6_Setup_win-x64_wavfix.exe`](https://github.com/xn1llox/Zenith-Audio-Player/releases/download/v1.0.6/ZenithAudio_v1.0.6_Setup_win-x64_wavfix.exe)

La versión `1.0.6` agrega fallback WAV PCM con NAudio cuando no están disponibles BASS/MPV, fallback DSF/DFF sin compresión DST a PCM en RAM, soporte de biblioteca para APE, WavPack, Opus y hojas CUE, y mejoras en los mensajes de backends opcionales.

## Características

- Interfaz moderna con WinUI 3 para Windows 10 y Windows 11.
- Dashboard audiófilo con pestañas de Escucha, Laboratorio Anti-Fake y Letras.
- Cadena de señal con LED de estado bit-perfect y flujo Archivo > Decoder > DSP > Salida.
- Escaneo de biblioteca local para FLAC, WAV, APE, WavPack, Opus, MP3, M4A, DSF/DFF, CUE y flujos SACD ISO.
- Selector de dispositivo de salida usando los dispositivos detectados por Windows.
- Conversión DSD a PCM cuando el equipo no tiene DAC o soporte DSD nativo.
- Fallback WAV PCM propio con NAudio para WAV que avanzan pero no suenan por Media Foundation.
- Controles de reproducción con barra de avance, volumen, aleatorio y cola.
- Panel de carátula y letras cuando la metadata está disponible.
- VU meter digital azul y visualizador de audio de fondo.
- ZenithAI, asistente de audio con configuración de API desde la app.
- Instalador Windows x64 creado con Inno Setup.

## Requisitos Para Ejecutar

### Mínimos

- Windows 10 versión 2004 / build 19041 o superior, 64 bits.
- CPU x64 de 2 núcleos.
- 4 GB de RAM.
- 500 MB libres para la app instalada.
- 1 GB libre adicional durante la instalación.
- Dispositivo de audio compatible con Windows Audio / WASAPI.
- Conexión a internet solo si se usará ZenithAI o si se descargan dependencias manualmente.

El instalador publicado incluye .NET Desktop Runtime 8 y Windows App Runtime 1.8, por lo que no deberían instalarse aparte.

### Recomendados

- Windows 11 64 bits actualizado, o Windows 10 22H2 64 bits.
- CPU x64 de 4 núcleos o superior.
- 8 GB de RAM o más.
- SSD para bibliotecas grandes.
- DAC USB, interfaz de audio o salida WASAPI estable para escucha dedicada.
- 2 GB libres para instalación, cachés temporales y futuras actualizaciones.
- Conexión a internet para ZenithAI mediante API compatible.

## Formatos Disponibles

| Formato | Extensiones | Estado |
| --- | --- | --- |
| FLAC | `.flac` | Soportado |
| WAV / PCM | `.wav` | Soportado |
| APE / Monkey's Audio | `.ape` | Soportado con BASS y `bass_ape.dll` |
| WavPack | `.wv` | Soportado con BASS y `bass_wv.dll`; archivos `.wvc` de correccion deben quedar junto al `.wv` |
| AIFF | `.aiff`, `.aif` | Soportado |
| ALAC / Apple Lossless | `.alac`, `.m4a` | Soportado según codecs disponibles |
| MP3 | `.mp3` | Soportado |
| AAC / MP4 audio | `.aac`, `.m4a` | Soportado según codecs disponibles |
| Ogg Opus | `.opus`, `.ogg` | Soportado según Windows Media Foundation o BASS con `bassopus.dll` |
| DSD DSF | `.dsf` | Soportado con conversión DSD a PCM si no hay DAC DSD |
| DSD DFF | `.dff` | Soportado con MPV o BASS + `bassdsd.dll`; fallback PCM para DFF/DSDIFF sin compresión DST |
| SACD ISO | `.iso` | Soportado mediante extracción a DSF con `sacd_extract.exe` |
| Hojas CUE | `.cue` | Soportado como índice virtual de pistas cuando referencia audio local |
| MQA | `.mqa`, `.flac` con MQA | Detectable/reproducible como PCM; decodificación MQA completa depende del DAC/backend |
| Letras sincronizadas | `.lrc` | Soportado |
| Letras simples | `.txt` | Soportado |
| Carátulas | `.jpg`, `.jpeg`, `.png`, metadata embebida | Soportado |

Notas:

- En equipos sin DAC DSD, Zenith Audio puede convertir DSD temporalmente a PCM para reproducir por Windows/Realtek/USB.
- Los formatos disponibles pueden variar según el backend activo y los codecs del sistema.
- MPV y BASS son backends experimentales/opcionales y sus binarios nativos no se distribuyen dentro del repositorio.
- FLAC/WAV multicanal pueden abrirse; en modo compartido Windows puede mezclar o bajar a estéreo según el dispositivo. En modo exclusivo, el DAC/interfaz debe aceptar el mapa de canales solicitado.

## Instalador y Actualizaciones

El instalador oficial se publica en GitHub Releases como archivo `.exe` para Windows x64.

Desde la versión `1.0.6`, el instalador:

- Detecta si Zenith Audio ya está instalado en el sistema.
- Muestra la versión instalada y la versión del instalador antes de continuar.
- Actualiza los archivos de la aplicación en el mismo directorio.
- Elimina archivos antiguos del directorio instalado antes de copiar los nuevos.
- Mantiene la configuración local del usuario guardada fuera del directorio de instalación.
- Instala o repara .NET Desktop Runtime 8 y Windows App Runtime 1.8 cuando corresponde.
- Se publica como `ZenithAudio_v1.0.6_Setup_win-x64_wavfix.exe` para distinguirlo de instaladores `1.0.6` anteriores.

La configuración local, cachés temporales y datos generados por el usuario se guardan en las rutas de usuario de Windows, no dentro de `Program Files`. El proceso de actualización limpia solamente el directorio de instalación de la app.

## Requisitos Para Desarrollo

- .NET 8 SDK.
- Visual Studio 2022 recomendado para desarrollo WinUI 3.
- Inno Setup 6 para compilar el instalador.
- Windows 10 build 19041+ o Windows 11.

## Compilar

```powershell
dotnet restore .\ZenithAudio.sln
dotnet build .\src\ZenithAudio\ZenithAudio.csproj -c Release -r win-x64
dotnet run --project .\src\ZenithAudio\ZenithAudio.csproj -c Release
```

## Crear Instalador

El script del instalador está en `installer/ZenithAudio.iss`.

Antes de compilar el instalador, prepara los redistribuibles necesarios:

```powershell
.\scripts\Prepare-InstallerRedist.ps1
dotnet publish .\src\ZenithAudio\ZenithAudio.csproj -c Release -r win-x64 --self-contained false
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' .\installer\ZenithAudio.iss
```

El instalador generado queda en `artifacts/installer/`.

## Herramientas Nativas

Los binarios nativos no se suben al repositorio. Si quieres probar motores o herramientas externas, colócalos en:

```text
src/ZenithAudio/runtimes/win-x64/native/
```

Archivos opcionales soportados:

- `sacd_extract.exe` para extraer SACD ISO a DSF.
- `mpv-2.dll` para pruebas con backend MPV.
- `bass.dll`, `basswasapi.dll`, `bassdsd.dll`, `bass_ape.dll`, `bass_wv.dll` y `bassopus.dll` para pruebas con backend BASS.

## ZenithAI

ZenithAI guarda la configuración de API de forma local y puede configurarse desde la app. No subas claves personales de API ni archivos locales generados.

## Manual de Usuario

- [Manual de usuario en Markdown](docs/MANUAL_DE_USUARIO.md)
- [Manual de usuario en PDF](docs/ZenithAudio_Manual_de_Usuario.pdf)

## Donaciones

Si quieres colaborar con el proyecto y ayudar a seguir sacando actualizaciones, puedes donar por cualquiera de estos medios:

- BTC: `bc1qqqwtvasyk2j0jdja6fyhkwg84qm53uwz4935d2`
- ETH: `0x0Ce533373C02D5069f193AF0a6e325bdAC8e8F4D`
- LTC: `ltc1qmac3zrd49n552c5xjpwm5n2p0d5kyydy5z6fah`
- PayPal: `felipeespinozaguajardo@gmail.com`

## Licencia

Este proyecto está licenciado bajo Apache License 2.0. Revisa [LICENSE](LICENSE).
