# Manual de Usuario de Zenith Audio Player

Versión del manual: 1.0.8  
Aplicación: Zenith Audio Player  
Sistema objetivo: Windows 10/11 64 bits

## 1. Qué es Zenith Audio

Zenith Audio Player es un reproductor de música audiófilo para Windows orientado a bibliotecas locales de alta resolución. Está diseñado para reproducir FLAC, WAV, AIFF, APE, WavPack, Opus, MP3, AAC/M4A, DSF/DFF, hojas CUE y flujos SACD ISO, con una interfaz OLED responsiva, selección de dispositivos de salida y motor estilo foobar2000 (**WASAPI** y **ASIO Nativo DSD64/DSD128**), medidor VU, ecualizador biquad, letras sincronizadas, carátulas, el asistente integrado **ZenithAI** multi-proveedor y **Control Remoto Web por red local (Wi-Fi / LAN)**.

La versión 1.0.8 incorpora motor ASIO nativo para DSD64/DSD128 (compatible con DACs USB como FiiO / JadeAudio JA11), decodificador DSD FIR de fase lineal Blackman-Harris, protección inteligente de Modo Exclusivo contra ruido blanco en salidas sin DAC dedicado, escaneo tolerante para bibliotecas de 60 GB+, interfaz OLED rediseñada con iconos vectoriales responsivos, integración directa de ZenithAI con el reproductor + extracción universal de modelos, y un servidor de Control Remoto Web con Tailwind CSS.

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
2. Ejecuta `ZenithAudio_v1.0.8_Setup_win-x64.exe` o una versión superior.
3. Acepta los permisos de administrador si Windows los solicita.
4. Espera a que el instalador agregue los componentes necesarios (`.NET Desktop Runtime 8` y `Windows App Runtime 1.8`).
5. Abre Zenith Audio desde el acceso directo del escritorio o desde el menú Inicio.

Si Windows SmartScreen muestra una advertencia, se debe a que la firma actual es local/autofirmada. La app no incluye telemetría ni instaladores ocultos, pero Windows puede advertir mientras no exista un certificado comercial OV/EV.

## 4. Primer inicio

Al abrir Zenith Audio verás estas zonas principales (con diseño responsivo que se adapta automáticamente a pantalla completa, pantalla dividida o ventana compacta):

- Barra superior: selector rápido de salida de audio, motor (`Zenith WASAPI`, `ASIO DSD Nativo`, `BASS`, `MPV`), interruptor de Modo Exclusivo, botones `Abrir`, `ZenithAI` y `Remoto`.
- Biblioteca y Explorador (izquierda): accesos rápidos (`Carpetas`, `DSD`, `Hi-Res PCM`, `Ajustes`), botones `+ Carpeta` / `Música Win`, buscador y lista de canciones.
- Escenario central: pista actual, carátula, Medidor VU Zenith, laboratorio Anti-Fake y letras sincronizadas.
- Panel derecho con 3 pestañas: `Señal` (cadena técnica y flujo de bits), `Tono / EQ` (ecualizador biquad y AutoEQ) y `Ajustes` (salida, driver ASIO, opciones DSD y buffer).
- Barra inferior: controles vectoriales de reproducción, línea de tiempo, volumen y ruta de audio activa.

## 5. Agregar música

1. En el Explorador de biblioteca (panel izquierdo), pulsa **`+ Carpeta`** para seleccionar cualquier carpeta de música o **`Música Win`** para cargar directamente tu carpeta Música de Windows.
2. También puedes pulsar **Carpetas** en el menú superior izquierdo para administrar tus carpetas guardadas.
3. Zenith Audio escaneará recursivamente la carpeta (tolerando subcarpetas del sistema o sin permisos) y recordará tus carpetas automáticamente al volver a abrir la app.

Para bibliotecas grandes (60 GB+), es recomendable usar un SSD y evitar escanear discos externos lentos mientras se copian archivos.

## 6. Buscar y reproducir canciones

- Usa el buscador del Explorador de biblioteca para filtrar por canción, álbum o formato.
- Haz doble clic sobre una canción para reproducirla.
- Usa los botones inferiores para reproducir, pausar, detener, avanzar o activar reproducción aleatoria.
- La barra de progreso permite adelantar o retroceder dentro de la pista cuando el backend activo lo permite.

## 7. Formatos soportados

| Formato | Extensiones | Estado |
| --- | --- | --- |
| FLAC | `.flac` | Soportado |
| WAV / PCM | `.wav` | Soportado; en 1.0.6 usa fallback PCM propio con NAudio si Media Foundation no entrega audio |
| APE / Monkey's Audio | `.ape` | Soportado con BASS y `bass_ape.dll` |
| WavPack | `.wv` | Soportado con BASS y `bass_wv.dll`; los `.wvc` deben estar junto al `.wv` |
| AIFF | `.aiff`, `.aif` | Soportado |
| ALAC / Apple Lossless | `.alac`, `.m4a` | Soportado según codecs disponibles |
| MP3 | `.mp3` | Soportado |
| AAC / MP4 Audio | `.aac`, `.m4a` | Soportado según codecs disponibles |
| Ogg Opus | `.opus`, `.ogg` | Soportado según Windows Media Foundation o BASS con `bassopus.dll` |
| DSD DSF | `.dsf` | Soportado: ASIO DSD64/DSD128 nativo, DoP Exclusivo o decodificación FIR Blackman-Harris a PCM |
| DSD DFF | `.dff` | Soportado: ASIO DSD nativo, DoP o decodificación FIR a PCM para DFF/DSDIFF sin compresión DST |
| SACD ISO | `.iso` | Soportado mediante extracción a DSF con `sacd_extract.exe` |
| Hojas CUE | `.cue` | Soportado como índice virtual de pistas cuando referencia audio local |
| MQA | `.mqa`, `.flac` con MQA | Reproducible como PCM; decodificación completa depende del DAC/backend |
| Letras sincronizadas | `.lrc` | Soportado |
| Letras simples | `.txt` | Soportado |
| Carátulas | `.jpg`, `.jpeg`, `.png`, metadata embebida | Soportado |

## 8. Salida de audio, WASAPI y ASIO Nativo

Desde la barra superior o en la pestaña derecha **Ajustes** puedes elegir salida y motor con cambio en caliente:

- **Windows predeterminado / Realtek / Headsets:** ideal con **Zenith WASAPI** en modo compartido.
- **DAC USB dedicado (ej. FiiO / JadeAudio JA11, Topping, iFi, SMSL):** compatible con **WASAPI Exclusivo** (`WAVE_FORMAT_EXTENSIBLE` PCM 32-bit / 24-in-32) y con **ASIO (DSD Nativo)** cuando el fabricante provee driver ASIO.
- **Protección de Modo Exclusivo:** el Modo Exclusivo inicia **desactivado por defecto**. Si intentas activarlo sobre una salida sin controlador DAC USB dedicado (como un jack 3.5 mm integrado o audífonos gaming), Zenith muestra una alerta y mantiene el Modo Exclusivo desactivado para evitar ruido blanco.
- **ASIO Nativo (DSD64 / DSD128):** selecciona el motor `ASIO (DSD Nativo)`, elige tu driver (por ejemplo `FiiO ASIO Driver`) y abre su panel de control ASIO directamente desde Ajustes si deseas configurar el tamaño de buffer.

## 9. DSD, FIR Blackman-Harris, DoP y SACD ISO

Desde la versión 1.0.8, Zenith Audio ofrece 3 rutas completas para archivos `.dsf` y `.dff`:

1. **ASIO DSD Nativo:** envía el flujo DSD64 / DSD128 directo al DAC sin conversión a PCM (volumen fijo al 100% para preservar señal bit-perfect).
2. **PCM FIR de fase lineal (Blackman-Harris):** decodificador multietapa propio con ganancia SACD seleccionable (`+0 dB`, `+3 dB`, `+6 dB`), limitador suave y frecuencia PCM objetivo (`Auto`, `44.1`, `88.2`, `176.4` o `352.8 kHz`). Funciona en cualquier salida de audio.
3. **DoP Exclusivo (DSD sobre PCM):** disponible sobre DACs USB dedicados compatibles con DoP en WASAPI Exclusivo.

Para SACD ISO, el proyecto usa `sacd_extract.exe` como herramienta externa colocada en `runtimes/win-x64/native/`. Si un archivo `.dff` usa compresión DST, requiere MPV o BASS con `bassdsd.dll`.

## 9.1 WAV y fallback PCM de la versión 1.0.6

Algunos archivos WAV Hi-Res, float o multicanal pueden avanzar en la barra de reproducción sin emitir sonido cuando Windows Media Foundation no negocia correctamente la salida. Desde la versión 1.0.6, Zenith usa un stream PCM propio con NAudio para WAV cuando no están disponibles BASS o MPV.

Este fallback:

- Lee el WAV directamente.
- Entrega PCM 16-bit al reproductor interno.
- Hace downmix básico a estéreo cuando el WAV tiene más de dos canales.
- Mantiene el VU meter con nivel real del stream.

Si usas un DAC o interfaz profesional y quieres salida nativa multicanal o bit-perfect estricta, instala un backend opcional como MPV o BASS.

## 9.2 Hojas CUE

Zenith puede abrir hojas `.cue` y crear pistas virtuales cuando el CUE referencia un archivo local compatible, por ejemplo un único `.flac`, `.wav`, `.ape` o `.wv` con varias pistas indexadas.

Para que funcione correctamente:

- Mantén el `.cue` en la misma carpeta que el archivo de audio referenciado.
- Conserva los nombres originales de archivo.
- Haz doble clic en una pista virtual para reproducir desde su índice.

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

Para que una letra externa se detecte automáticamente, usa el mismo nombre base exacto de la canción. Solo debe cambiar la extensión:

```text
01 Canción.flac
01 Canción.lrc
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

ZenithAI es un asistente audiófilo integrado directamente con Zenith Audio mediante un panel flotante de 3 vistas (**Chat**, **Acciones** y **Config API**) y un **mini-reproductor sincronizado en vivo**.

Puede ayudarte tanto con consultas técnicas/musicales como controlando el reproductor en tiempo real:

- Reproducir canciones de tu biblioteca cargada (*"pon Dark Side of the Moon"*), pausar, avanzar, retroceder, activar aleatorio o cambiar el volumen.
- Cambiar motor (`WASAPI`, `ASIO`, `BASS`, `MPV`), dispositivo de salida, modo/ganancia/frecuencia DSD o lanzar el análisis Anti-Fake.
- Aplicar perfiles tonales y ecualización biquad al instante con preamp anti-clipping.
- En **Config API**, elegir entre 11 proveedores preconfigurados (`NVIDIA NIM`, `OpenAI`, `OpenRouter`, `Groq`, `Google Gemini`, `DeepSeek`, `Mistral`, `Together`, `Ollama`, `LM Studio` y `Personalizado`), pulsar **`Extraer modelos`** para cargar y filtrar el catálogo real de cualquier API, y verificar la conexión con **`Probar`**.

Las claves API se cifran localmente por usuario en Windows y nunca se suben a GitHub. En servidores locales (`Ollama` / `LM Studio`) no se requiere API key.

## 14.1 Control Remoto Web (Wi-Fi / LAN)

Desde la versión 1.0.8 puedes controlar Zenith Audio de forma inalámbrica desde tu teléfono, tablet u otro equipo en la misma red Wi-Fi:

1. Pulsa el botón **`Remoto`** ubicado junto a `ZenithAI` en la barra superior.
2. Pulsa **`Iniciar servidor remoto`** (puerto por defecto `8787`, no requiere permisos de administrador).
3. Si Windows Defender Firewall muestra un aviso la primera vez, permite el acceso en **Redes privadas**.
4. Abre la dirección mostrada (`http://192.168.x.x:8787`) en el navegador de tu teléfono.

La interfaz web responsiva (estilizada con Tailwind CSS en paleta OLED oscura + cian) ofrece 3 pestañas:

- **Ahora:** carátula en vivo, barra VU, línea de tiempo, controles de reproducción, letra sincronizada y control de volumen.
- **Canciones:** búsqueda instantánea en tu biblioteca cargada con filtros `Todas`, `DSD` y `Hi-Res PCM`.
- **Audio:** cambio en vivo de salida de audio, motor (`WASAPI` / `ASIO` / `BASS` / `MPV`), driver ASIO, Modo Exclusivo (con protección DAC), ajustes DSD y ecualizador biquad.

Opcionalmente puedes definir un **PIN de acceso** o activar el **inicio automático** del servidor desde el mismo panel.

## 15. Fundamentos Técnicos de las Funciones Principales

Esta sección explica por qué existen las funciones de Zenith Audio y cómo afectan la reproducción. Está escrita como referencia técnica para usuarios que quieren entender qué ocurre entre el archivo musical, el sistema operativo y el dispositivo de salida.

### 15.1 Control de tono

El Control de tono modifica la respuesta tonal antes de enviar la señal al dispositivo de salida. En términos prácticos, permite ajustar zonas de frecuencia para adaptar la reproducción al equipo, sala o audífonos.

Funciones principales:

- EQ: activa o desactiva la ecualización.
- Omitir DSP: deja pasar la señal sin procesamiento tonal adicional.
- Preajuste: selecciona una curva base, por ejemplo una referencia plana.
- Preamplificación: reduce o aumenta el nivel general antes del procesamiento.
- Subgraves: ajusta la zona grave profunda, útil para compensar audífonos o altavoces con poca extensión baja.
- Presencia: modifica la zona media-alta donde suelen percibirse voces, guitarras y ataque de instrumentos.
- Aire: ajusta la zona alta, asociada a brillo, detalle y sensación de espacio.

Uso recomendado:

- Para escucha crítica: usa perfil plano y DSP omitido.
- Para audífonos con exceso de brillo: reduce Aire o Presencia.
- Para altavoces pequeños: aumenta Subgraves con moderación.
- Si aparece saturación o distorsión: baja la Preamplificación antes de subir bandas de EQ.

La ecualización no mejora la calidad original del archivo; solo modifica la respuesta percibida. Bien usada, puede corregir limitaciones del equipo. Mal usada, puede producir fatiga, clipping o una presentación menos natural.

### 15.2 Ajustes del sistema

Los Ajustes del sistema controlan cómo Zenith Audio se comunica con Windows y con el dispositivo de audio.

Elementos principales:

- Dispositivo de salida: permite elegir entre altavoces Realtek, audífonos USB, DAC externo, interfaz de audio o dispositivo predeterminado de Windows.
- Formato preferido: define si se intenta respetar el formato fuente o si se usa una salida compatible con Windows.
- Modo DSD: determina si se intenta DSD nativo, DoP o conversión a PCM.
- Modo exclusivo: intenta reservar el dispositivo para Zenith Audio y reducir intervención del mezclador de Windows.

En modo compartido, Windows puede mezclar el audio de varias aplicaciones y convertirlo al formato configurado en el sistema. En modo exclusivo, Zenith Audio intenta acceder al dispositivo de forma directa. Esto puede mejorar la fidelidad de ruta, pero también puede impedir que otras apps suenen mientras la reproducción está activa.

### 15.3 Buffer

El buffer es una zona temporal de memoria donde se guardan pequeñas porciones de audio antes de enviarlas al dispositivo. Su objetivo es evitar cortes cuando el sistema operativo, el motor de audio o el disco tardan algunos milisegundos en entregar datos.

Un buffer más pequeño:

- Reduce la latencia.
- Hace que los controles respondan con mayor rapidez.
- Exige más estabilidad del sistema y del driver.
- Puede producir cortes si el equipo está bajo carga.

Un buffer más grande:

- Aumenta estabilidad.
- Reduce riesgo de clics o interrupciones.
- Aumenta la latencia.
- Puede hacer que cambios de pista, seek o pausa se sientan menos inmediatos.

Para reproducción musical, una latencia extremadamente baja no siempre es necesaria. Es más importante evitar cortes, mantener estabilidad y conservar una ruta de salida compatible con el dispositivo.

### 15.4 Latencia

La latencia es el tiempo entre una acción o dato de audio y su salida audible por el dispositivo. En reproducción musical local, la latencia no afecta la calidad del archivo, pero sí la sensación de respuesta.

Factores que influyen:

- Tamaño del buffer.
- Driver del dispositivo.
- Modo compartido o exclusivo.
- Conversión DSD a PCM.
- Carga de CPU.
- Velocidad del disco o SSD.
- Procesamiento de EQ o visualización.

En modo exclusivo con buffer pequeño, la latencia puede depender directamente del tamaño del buffer. Por eso, reducir el buffer sin un driver estable puede producir fallos.

### 15.5 PCM

PCM significa Pulse Code Modulation. Es la forma más común de representar audio digital. En PCM, la señal analógica se mide muchas veces por segundo y cada medición se guarda con una profundidad de bits.

Parámetros principales:

- Frecuencia de muestreo: cuántas muestras por segundo se toman. Ejemplos: 44.1 kHz, 48 kHz, 96 kHz, 192 kHz.
- Profundidad de bits: cuántos niveles posibles tiene cada muestra. Ejemplos: 16 bit, 24 bit, 32 bit float.
- Canales: mono, estéreo o multicanal.

Ejemplos de formatos basados en PCM:

- WAV.
- FLAC.
- AIFF.
- ALAC.

FLAC y ALAC son formatos sin pérdida: comprimen PCM sin eliminar información musical, de forma similar a un ZIP especializado para audio.

### 15.6 DSD nativo

DSD significa Direct Stream Digital. A diferencia de PCM, DSD usa una señal de 1 bit a una frecuencia muy alta. Es el formato asociado a SACD y a archivos DSF/DFF.

Ejemplos:

- DSD64: 2.8224 MHz.
- DSD128: 5.6448 MHz.
- DSD256: 11.2896 MHz.

DSD nativo significa que el flujo DSD llega al DAC sin convertirse previamente a PCM. Para eso se necesita:

- DAC compatible con DSD.
- Driver compatible.
- Backend de audio capaz de enviar DSD nativo o DoP.
- Configuración correcta de salida.

Si el equipo solo tiene salida Realtek, altavoces internos o un dispositivo USB sin soporte DSD, Zenith Audio convierte temporalmente DSD a PCM para que el archivo pueda reproducirse.

### 15.7 Diferencia entre PCM y DSD nativo

PCM y DSD son formas distintas de representar audio digital. No son simplemente “mejor” o “peor”; dependen del master, del DAC, del driver y del flujo de reproducción.

Comparación práctica:

| Aspecto | PCM | DSD nativo |
| --- | --- | --- |
| Representación | Muestras multibit | Flujo 1 bit de muy alta frecuencia |
| Uso común | FLAC, WAV, AIFF, ALAC | SACD, DSF, DFF |
| Compatibilidad | Muy alta | Requiere DAC/driver compatible |
| Edición/mastering | Directa y común | Normalmente requiere conversión para edición |
| Reproducción en Windows estándar | Directa | Suele requerir conversión o backend especializado |
| Tamaño de archivo | Variable; FLAC reduce tamaño sin pérdida | Generalmente grande |

En la práctica, un buen master PCM puede sonar mejor que un mal master DSD, y un buen master DSD puede ofrecer una presentación excelente si el DAC y la cadena están preparados. La calidad final depende más del master y de la cadena completa que del nombre del formato.

### 15.8 Bit-perfect, modo compartido y modo exclusivo

Bit-perfect significa que las muestras digitales llegan al dispositivo sin cambios innecesarios de volumen, mezcla, efectos o remuestreo. Para acercarse a esa ruta, conviene:

- Desactivar efectos de sonido del sistema.
- Usar modo exclusivo cuando el dispositivo lo soporte.
- Evitar mover el volumen digital si se busca máxima fidelidad.
- Usar formato de salida compatible con el archivo o con el DAC.

En modo compartido, Windows puede mezclar sonidos de varias aplicaciones. En modo exclusivo, la app intenta reservar el endpoint de audio. Esto puede reducir procesamiento intermedio, pero exige que el dispositivo acepte el formato solicitado.

### 15.9 Interpretación del Medidor Zenith

El Medidor Zenith es una visualización musical en tiempo real. Su objetivo es mostrar actividad dinámica de la pista y entregar una referencia visual del nivel.

No debe interpretarse como:

- Medidor profesional de mastering.
- Medidor legal de broadcast.
- Medidor exacto de loudness LUFS.

Debe interpretarse como:

- Indicador visual de energía.
- Referencia de dinámica general.
- Complemento estético para la escucha.

### 15.10 Buenas prácticas de escucha

- Usa archivos sin pérdida cuando sea posible: FLAC, WAV, AIFF, ALAC, DSF.
- Evita subir EQ y volumen al máximo al mismo tiempo.
- Si usas audífonos, evita niveles altos prolongados.
- Para Windows compartido, configura una frecuencia compatible con tu biblioteca habitual.
- Para DAC dedicado, prueba modo exclusivo y compara estabilidad.
- Para DSD sin DAC compatible, usa conversión a PCM sin asumir pérdida audible automática: una conversión bien hecha puede ser transparente en muchos escenarios prácticos.

## 16. Solución de problemas

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

En 1.0.6, DSF y DFF/DSDIFF sin DST pueden bajar a PCM en RAM. Si el DFF usa DST, instala MPV o BASS con `bassdsd.dll`.

### El WAV avanza pero no suena

Instala la versión 1.0.6 o superior. Esa versión agrega fallback WAV PCM con NAudio para evitar depender de Media Foundation en WAV problemáticos.

### BASS o MPV aparecen como opcionales

BASS y MPV son backends opcionales. Si no están instalados, Zenith usa rutas de fallback de Windows/NAudio cuando el formato lo permite. Para DSD nativo, APE, WavPack avanzado u Opus por BASS, coloca las DLL correspondientes en `src/ZenithAudio/runtimes/win-x64/native/` durante desarrollo o junto a la app instalada.

### SACD ISO no se abre

Puede faltar `sacd_extract.exe` o el ISO puede no ser compatible. Prueba con una release reciente del instalador o revisa las herramientas nativas opcionales.

### No puedo activar Modo Exclusivo (alerta de controlador DAC)

Zenith Audio bloquea por seguridad el Modo Exclusivo en salidas integradas (como jack 3.5 mm Realtek o audífonos gaming) para evitar ruido blanco. Selecciona tu DAC USB dedicado en el selector superior o utiliza el motor **ASIO (DSD Nativo)** con el driver del fabricante.

### Mi teléfono no carga el Control Remoto Web

Revisa:

- Que el servidor remoto esté **ACTIVO** desde el botón `Remoto` junto a `ZenithAI`.
- Que el teléfono y el PC estén conectados a la misma red Wi-Fi.
- Que la opción **Permitir acceso por red local Wi-Fi (0.0.0.0)** esté activada.
- Que el Firewall de Windows permita conexiones entrantes a `ZenithAudio.exe` en **Redes privadas**.

### ZenithAI no responde

Revisa:

- Conexión a internet (o que Ollama / LM Studio estén abiertos si usas un servidor local).
- Endpoint, modelo seleccionado y API key en **`ZenithAI → Config API`**.
- Usa el botón **`Probar`** en `Config API` para comprobar la respuesta directa del proveedor.

## 17. Actualizaciones

Las nuevas versiones se publican en GitHub Releases:

```text
https://github.com/xn1llox/Zenith-Audio-Player/releases
```

Descarga siempre la versión más reciente si usas Windows 10 o si encuentras problemas con runtime, acentos, letras o reproducción.

## 18. Donaciones

Si quieres colaborar con el proyecto y ayudar a seguir sacando actualizaciones:

- BTC: `bc1qqqwtvasyk2j0jdja6fyhkwg84qm53uwz4935d2`
- ETH: `0x0Ce533373C02D5069f193AF0a6e325bdAC8e8F4D`
- LTC: `ltc1qmac3zrd49n552c5xjpwm5n2p0d5kyydy5z6fah`
- PayPal: `felipeespinozaguajardo@gmail.com`

## 19. Créditos

Desarrollado por **xn1llox** — [www.espinozainformatica.cl](https://www.espinozainformatica.cl)

Proyecto publicado bajo Apache License 2.0.

## 20. Bibliografía y Referencias

1. Microsoft Learn. “AUDCLNT_SHAREMODE enumeration”. Documentación de Core Audio APIs / WASAPI. Disponible en: https://learn.microsoft.com/en-us/windows/desktop/api/Audiosessiontypes/ne-audiosessiontypes-audclnt_sharemode

2. Microsoft Learn. “IAudioClient::Initialize method”. Documentación de WASAPI para inicialización de streams, buffers y latencia. Disponible en: https://learn.microsoft.com/en-us/previous-versions/ms678736(v=vs.85)

3. Microsoft Learn. “IAudioClient interface”. Descripción de streams compartidos y exclusivos en Windows Audio. Disponible en: https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iaudioclient

4. Microsoft Learn. “Windows App SDK deployment guide for framework-dependent apps packaged with external location or unpackaged”. Referencia de despliegue para Windows App Runtime. Disponible en: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps

5. Sony Corporation. “Direct Stream Digital”. Referencia técnica introductoria sobre DSD y su diferencia frente a PCM convencional. Disponible en: https://www.sony.co.jp/en/Products/DSD/

6. Merging Technologies. “Super Audio CD Production Using Direct Stream Digital Technology”. Documento técnico sobre producción SACD/DSD. Disponible en: https://www.merging.com/uploads/assets/Merging_pdfs/dsd1.pdf

7. Smith, Julius O. “Mathematics of the Discrete Fourier Transform (DFT) with Audio Applications”. W3K Publishing. Referencia abierta sobre fundamentos de audio digital, muestreo y procesamiento. Disponible en: https://ccrma.stanford.edu/~jos/mdft/

8. Pohlmann, Ken C. “Principles of Digital Audio”. McGraw-Hill. Referencia bibliográfica general sobre audio digital, PCM, muestreo, cuantización y conversión.
