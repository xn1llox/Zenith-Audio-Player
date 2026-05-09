# Manual de Usuario de Zenith Audio Player

Versión del manual: 1.0  
Aplicación: Zenith Audio Player  
Sistema objetivo: Windows 10/11 64 bits

## 1. Qué es Zenith Audio

Zenith Audio Player es un reproductor de música para Windows orientado a bibliotecas locales de alta resolución. Está diseñado para reproducir FLAC, WAV, AIFF, MP3, AAC/M4A, DSF/DFF y flujos SACD ISO, con una interfaz moderna, control de dispositivos de salida, visualización tipo VU meter, letras, carátulas y el asistente ZenithAI.

El objetivo principal es ofrecer una experiencia de escucha clara, técnica y fácil de usar, especialmente para usuarios que tienen bibliotecas grandes, archivos DSD o equipos de audio dedicados.

## 2. Requisitos

### Requisitos mínimos

- Windows 10 versión 2004 / build 19041 o superior, 64 bits.
- CPU x64 de 2 núcleos.
- 4 GB de RAM.
- 500 MB libres para la app instalada.
- 1 GB libre adicional durante la instalación.
- Dispositivo de audio compatible con Windows Audio / WASAPI.

### Requisitos recomendados

- Windows 11 64 bits actualizado, o Windows 10 22H2 64 bits.
- CPU x64 de 4 núcleos o superior.
- 8 GB de RAM o más.
- SSD para bibliotecas grandes.
- DAC USB, interfaz de audio o salida WASAPI estable.
- Conexión a internet si se usará ZenithAI.

El instalador oficial incluye .NET Desktop Runtime 8 y Windows App Runtime 1.8, por lo que normalmente no necesitas instalarlos por separado.

## 3. Instalación

1. Descarga el instalador desde la sección Releases del repositorio de GitHub.
2. Ejecuta `ZenithAudio_vX.X.X_Setup_win-x64.exe`.
3. Acepta los permisos de administrador si Windows los solicita.
4. Espera a que el instalador agregue los componentes necesarios.
5. Abre Zenith Audio desde el acceso directo del escritorio o desde el menú Inicio.

Si Windows SmartScreen muestra una advertencia, se debe a que la firma actual es local/autofirmada. La app no incluye telemetría ni instaladores ocultos, pero Windows puede advertir mientras no exista un certificado comercial OV/EV.

## 4. Primer inicio

Al abrir Zenith Audio verás estas zonas principales:

- Biblioteca: accesos a carpetas de música, álbumes DSD, Hi-Res PCM y ajustes.
- Explorador de biblioteca: lista de canciones detectadas.
- Reproduciendo ahora: pista actual, carátula, VU meter y letras.
- Control de tono: EQ, preamplificación, subgraves, presencia y aire.
- Ajustes del sistema: salida de audio, formato preferido, modo DSD y opciones avanzadas.
- Barra inferior: controles de reproducción, progreso, volumen y estado de ruta de audio.

## 5. Agregar música

1. En el panel izquierdo, abre Biblioteca.
2. Entra en Carpetas de música.
3. Agrega una carpeta donde tengas archivos de audio.
4. Zenith Audio escaneará la carpeta y mostrará las pistas detectadas.

Para bibliotecas grandes, es recomendable usar un SSD y evitar escanear discos externos lentos mientras se copian archivos.

## 6. Buscar y reproducir canciones

- Usa el buscador del Explorador de biblioteca para filtrar por canción, álbum o formato.
- Haz doble clic sobre una canción para reproducirla.
- Usa los botones inferiores para reproducir, pausar, detener, avanzar o activar reproducción aleatoria.
- La barra de progreso permite adelantar o retroceder dentro de la pista cuando el backend activo lo permite.

## 7. Formatos soportados

| Formato | Extensiones | Estado |
| --- | --- | --- |
| FLAC | `.flac` | Soportado |
| WAV / PCM | `.wav` | Soportado |
| AIFF | `.aiff`, `.aif` | Soportado |
| ALAC / Apple Lossless | `.alac`, `.m4a` | Soportado según codecs disponibles |
| MP3 | `.mp3` | Soportado |
| AAC / MP4 Audio | `.aac`, `.m4a` | Soportado según codecs disponibles |
| DSD DSF | `.dsf` | Soportado con conversión DSD a PCM si no hay DAC DSD |
| DSD DFF | `.dff` | Soportado parcialmente según backend disponible |
| SACD ISO | `.iso` | Soportado mediante extracción a DSF con `sacd_extract.exe` |
| MQA | `.mqa`, `.flac` con MQA | Reproducible como PCM; decodificación completa depende del DAC/backend |
| Letras sincronizadas | `.lrc` | Soportado |
| Letras simples | `.txt` | Soportado |
| Carátulas | `.jpg`, `.jpeg`, `.png`, metadata embebida | Soportado |

## 8. Salida de audio

En Ajustes del sistema puedes seleccionar el dispositivo de salida.

- Windows predeterminado: usa la salida activa del sistema.
- Realtek / altavoces integrados: recomendado para equipos sin DAC externo.
- USB DAC / interfaz de audio: recomendado para escucha dedicada.
- WASAPI exclusivo: intenta entregar audio directamente al dispositivo, evitando mezclas del sistema.

Si no tienes DAC DSD, Zenith Audio puede convertir archivos DSD a PCM para reproducirlos igualmente por Windows.

## 9. DSD, SACD ISO y conversión temporal

Los archivos DSF/DFF y SACD ISO pueden requerir conversión si el equipo no tiene soporte nativo DSD.

Zenith Audio intenta:

1. Detectar el formato del archivo.
2. Revisar el dispositivo de salida.
3. Usar una ruta compatible.
4. Convertir temporalmente a PCM cuando sea necesario.

Para SACD ISO, el proyecto usa `sacd_extract.exe` como herramienta externa. Por licencia y distribución, este binario puede no venir dentro del repositorio fuente, pero el instalador oficial puede incluir lo necesario según la release publicada.

## 10. Carátulas

Zenith Audio intenta mostrar carátulas desde:

- Metadata embebida en el archivo.
- Archivos locales como `cover.jpg`, `cover.png`, `folder.jpg` o imágenes equivalentes en la carpeta del álbum.

Si no se encuentra imagen, se muestra un panel visual de Zenith como marcador.

## 11. Letras

Zenith Audio puede mostrar:

- Letras embebidas en la metadata.
- Archivos `.lrc` sincronizados con tiempo.
- Archivos `.txt` simples.

Para que una letra externa se detecte automáticamente, usa el mismo nombre base de la canción:

```text
01 Cancion.flac
01 Cancion.lrc
```

También se aceptan archivos genéricos como `lyrics.lrc` o `lyrics.txt` dentro de la carpeta del álbum.

## 12. VU Meter y visualizador

El Medidor Zenith muestra actividad de audio con una estética inspirada en medidores audiófilos. La aguja usa suavizado para evitar movimientos bruscos y seguir la dinámica general de la música.

El valor no reemplaza un medidor profesional de mastering; está pensado como visualización musical en tiempo real.

## 13. Control de tono

El panel Control de tono permite ajustar:

- EQ activado/desactivado.
- Omisión DSP.
- Preajuste.
- Preamplificación.
- Subgraves.
- Presencia.
- Aire.

Si buscas una reproducción lo más fiel posible, usa perfil plano y DSP omitido. Si escuchas con audífonos o altavoces pequeños, los controles de tono pueden ayudar a compensar el equipo.

## 14. ZenithAI

ZenithAI es un asistente integrado enfocado en audio. Puede ayudar con:

- Historia musical.
- Formatos de audio.
- Dudas sobre DSD, FLAC, PCM y DACs.
- Sugerencias para escuchar mejor.
- Interpretación general de la biblioteca cargada, usando solo la información que Zenith Audio le entrega.

ZenithAI usa API externa configurable. Las claves se guardan localmente y no deben subirse a GitHub.

## 15. Solución de problemas

### La app no abre en Windows 10

Instala la versión 1.0.1 o superior. Desde esa versión el instalador incluye Windows App Runtime 1.8.

### Aparecen caracteres raros en letras o acentos

Instala la versión 1.0.2 o superior. Esa versión mejora la lectura UTF-8, UTF-16 y Latin-1.

### No se reproduce DSD

Revisa:

- Que el archivo no esté corrupto.
- Que el dispositivo de salida esté seleccionado.
- Que el modo de reproducción DSD esté en automático o conversión a PCM.
- Que el volumen del sistema y de la app estén activos.

### SACD ISO no se abre

Puede faltar `sacd_extract.exe` o el ISO puede no ser compatible. Prueba con una release reciente del instalador o revisa las herramientas nativas opcionales.

### ZenithAI no responde

Revisa:

- Conexión a internet.
- Endpoint/API configurado.
- Clave API válida.
- Tiempo de espera de la API externa.

## 16. Actualizaciones

Las nuevas versiones se publican en GitHub Releases:

```text
https://github.com/xn1llox/Zenith-Audio-Player/releases
```

Descarga siempre la versión más reciente si usas Windows 10 o si encuentras problemas con runtime, acentos, letras o reproducción.

## 17. Donaciones

Si quieres colaborar con el proyecto y ayudar a seguir sacando actualizaciones:

- BTC: `bc1qqqwtvasyk2j0jdja6fyhkwg84qm53uwz4935d2`
- ETH: `0x0Ce533373C02D5069f193AF0a6e325bdAC8e8F4D`
- LTC: `ltc1qmac3zrd49n552c5xjpwm5n2p0d5kyydy5z6fah`
- PayPal: `felipeespinozaguajardo@gmail.com`

## 18. Créditos

Desarrollado por Felipe Espinoza `XN1ll0X` desde Chile.

Proyecto publicado bajo Apache License 2.0.
