# Zenith Audio Player

Zenith Audio Player es un reproductor de música para Windows creado con C#, WinUI 3, XAML y Windows App SDK. El proyecto está enfocado en bibliotecas locales de alta resolución, reproducción DSD/DSF, soporte para extracción de SACD ISO, configuración de dispositivos de audio, visualización tipo audiófila, letras, carátulas y un asistente opcional llamado ZenithAI.

## Características

- Interfaz moderna con WinUI 3 para Windows 10 y Windows 11.
- Escaneo de biblioteca local para FLAC, WAV, MP3, M4A, DSF/DFF y flujos SACD ISO.
- Selector de dispositivo de salida usando los dispositivos detectados por Windows.
- Conversión DSD a PCM cuando el equipo no tiene DAC o soporte DSD nativo.
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
| AIFF | `.aiff`, `.aif` | Soportado |
| ALAC / Apple Lossless | `.alac`, `.m4a` | Soportado según codecs disponibles |
| MP3 | `.mp3` | Soportado |
| AAC / MP4 audio | `.aac`, `.m4a` | Soportado según codecs disponibles |
| DSD DSF | `.dsf` | Soportado con conversión DSD a PCM si no hay DAC DSD |
| DSD DFF | `.dff` | Soportado parcialmente según backend disponible |
| SACD ISO | `.iso` | Soportado mediante extracción a DSF con `sacd_extract.exe` |
| MQA | `.mqa`, `.flac` con MQA | Detectable/reproducible como PCM; decodificación MQA completa depende del DAC/backend |
| Letras sincronizadas | `.lrc` | Soportado |
| Letras simples | `.txt` | Soportado |
| Carátulas | `.jpg`, `.jpeg`, `.png`, metadata embebida | Soportado |

Notas:

- En equipos sin DAC DSD, Zenith Audio puede convertir DSD temporalmente a PCM para reproducir por Windows/Realtek/USB.
- Los formatos disponibles pueden variar según el backend activo y los codecs del sistema.
- MPV y BASS son backends experimentales/opcionales y sus binarios nativos no se distribuyen dentro del repositorio.

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

## Instalador

El script del instalador está en `installer/ZenithAudio.iss`.

Antes de compilar el instalador, descarga los redistribuibles necesarios:

```powershell
.\scripts\Prepare-InstallerRedist.ps1
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
- `bass.dll`, `basswasapi.dll` y `bassdsd.dll` para pruebas con backend BASS.

## ZenithAI

ZenithAI guarda la configuración de API de forma local y puede configurarse desde la app. No subas claves personales de API ni archivos locales generados.

## Donaciones

Si quieres colaborar con el proyecto y ayudar a seguir sacando actualizaciones, puedes donar por cualquiera de estos medios:

- BTC: `bc1qqqwtvasyk2j0jdja6fyhkwg84qm53uwz4935d2`
- ETH: `0x0Ce533373C02D5069f193AF0a6e325bdAC8e8F4D`
- LTC: `ltc1qmac3zrd49n552c5xjpwm5n2p0d5kyydy5z6fah`
- PayPal: `felipeespinozaguajardo@gmail.com`

## Licencia

Este proyecto está licenciado bajo Apache License 2.0. Revisa [LICENSE](LICENSE).
