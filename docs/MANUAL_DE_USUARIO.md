# Manual de Usuario de Zenith Audio Player

Versión del manual: **1.0.8**  
Aplicación: **Zenith Audio Player**  
Sistema objetivo: **Windows 10 / Windows 11 (64 bits, x64)**  
Desarrollado por: **xn1llox** — [www.espinozainformatica.cl](https://www.espinozainformatica.cl)

## 1. Qué es Zenith Audio

Zenith Audio Player es un reproductor de música audiófilo para Windows construido con C#, .NET 8, WinUI 3 y Windows App SDK 1.8. Está diseñado como una mesa de escucha local de alta resolución orientada a colecciones extensas de FLAC, WAV, AIFF, ALAC, APE, WavPack, Opus, MP3, AAC/M4A, DSD (`.dsf` / `.dff`), hojas `.cue` y flujos SACD `.iso`.

### Principales novedades de la versión 1.0.8

- **Motor de audio estilo foobar2000 con cambio en caliente:** selección rápida de salida de audio y motor desde la barra superior sin reiniciar la aplicación.
- **ASIO Nativo (DSD64 / DSD128 y PCM):** comunicación directa COM `IASIO` con controladores ASIO de hardware (como FiiO / JadeAudio JA11), selector de driver y acceso directo al panel de control ASIO.
- **Decodificador DSD/DSF/DFF reconstruido:** filtro FIR multietapa de fase lineal Blackman-Harris, ganancia SACD configurable (`+0 / +3 / +6 dB`), frecuencias PCM objetivo (`Auto`, `44.1`–`352.8 kHz`) y **DoP v1.1 Exclusivo**.
- **Pipeline de audio optimizado en RAM:** buffer de pre-lectura desacoplado (~550 ms) con latencia de hardware WASAPI ajustada (`45–90 ms`), lector DSD secuencial de `512 KB`, eliminación de chasquidos eléctricos en *seek* (silencio DSD `0x69` sin DC + rampa cosenoidal *de-click*) y arrastre fluido sin microcortes.
- **Protección inteligente de Modo Exclusivo:** inicia desactivado por defecto, inspecciona el controlador del endpoint y bloquea el modo exclusivo sobre salidas integradas (jack 3.5 mm / headsets) para prevenir ruido blanco.
- **Ecualizador biquad real de 3 bandas + Preamp** con bypass bit-transparente y perfiles AutoEQ.
- **Biblioteca tolerante para carpetas de 60 GB+:** botones rápidos `+ Carpeta` / `Música Win`, persistencia automática de carpetas y tolerancia a subcarpetas protegidas del sistema.
- **Interfaz OLED responsiva:** barra de título oscura de Windows 11, iconos vectoriales Hi-Fi, panel derecho organizado en 3 pestañas (`Señal`, `Tono / EQ`, `Ajustes`) y adaptación automática a ventana dividida o compacta.
- **ZenithAI integrado con el reproductor:** nuevo menú de 3 vistas (`Chat`, `Acciones`, `Config API`), mini-reproductor sincronizado en vivo, control directo de pistas/salida/motor/EQ, 11 proveedores preconfigurados (incluyendo Ollama y LM Studio locales) y extracción universal de modelos.
- **Control Remoto Web (Wi-Fi / LAN) con Tailwind CSS:** servidor HTTP embebido sin permisos de administrador (`Remoto` junto a `ZenithAI`) para controlar volumen, canciones, salidas, motor, DSD y EQ desde tu teléfono.
- **Actualizador automático desde GitHub Releases:** comprobación al iniciar o manual, descarga con barra de progreso e instalación silenciosa con reapertura automática.

## 2. Requisitos del sistema

### Requisitos mínimos

- Windows 10 versión 2004 (build 19041) o superior, 64 bits (x64).
- Procesador x64 de 2 núcleos.
- 4 GB de memoria RAM.
- 500 MB libres para la instalación y 1 GB temporal durante el proceso.
- Dispositivo de audio compatible con Windows Core Audio / WASAPI.

### Requisitos recomendados

- Windows 11 (64 bits) actualizado o Windows 10 22H2 (64 bits).
- Procesador x64 de 4 núcleos o superior (recomendado para DSD128/DSD256 FIR).
- 8 GB de RAM o más y unidad SSD para escaneo instantáneo de bibliotecas masivas.
- DAC USB dedicado con controlador USB Audio 2.0 / ASIO (ej. FiiO / JadeAudio, Topping, iFi, SMSL) para reproducción bit-perfect y DSD nativo.
- Red Wi-Fi local si usarás el Control Remoto Web desde un teléfono, y acceso a internet para ZenithAI en la nube y el actualizador de GitHub.

> El instalador completo oficial incluye **.NET Desktop Runtime 8.0.25 x64** y **Windows App Runtime 1.8 x64**, por lo que no necesitas instalar dependencias adicionales.

## 3. Instalación y Actualizador automático

### Instalación inicial

1. Descarga `ZenithAudio_v1.0.8_Setup_win-x64.exe` desde [GitHub Releases](https://github.com/xn1llox/Zenith-Audio-Player/releases).
2. Ejecuta el instalador y acepta los permisos de administrador solicitados por Windows.
3. Si ya tienes una versión anterior instalada, el instalador reemplaza los binarios antiguos conservando intacta tu configuración en `%LOCALAPPDATA%\ZenithAudio\`.
4. Al finalizar, inicia **Zenith Audio** desde el acceso directo del escritorio o el menú Inicio.

### Actualizador automático desde GitHub

En la pestaña derecha **Ajustes → ACTUALIZACIONES (GITHUB)** dispones de un actualizador integrado:

- **Buscar actualizaciones al iniciar:** activado por defecto; consulta `releases/latest` en segundo plano 2.5 segundos después de abrir la app sin retrasar el arranque ni afectar la reproducción sin conexión.
- **Descargar e instalar automáticamente:** desactivado por defecto (*Preguntar primero*). Si lo activas, descarga e instala silenciosamente cuando aparezca una nueva versión.
- **Buscar actualizaciones (manual):** comprueba al instante si existe un release más reciente, muestra las notas de versión y habilita el botón **Descargar e instalar**.
- **Instalación silenciosa limpia:** la descarga se realiza con barra de progreso (MB y %) en `%TEMP%\ZenithAudioUpdates\`, detiene el audio para liberar el DAC/ASIO, ejecuta Inno Setup en modo silencioso (`/SILENT /CLOSEAPPLICATIONS`) y reabre Zenith Audio automáticamente al terminar.

## 4. Interfaz OLED y diseño responsivo

Zenith Audio adapta automáticamente su distribución al tamaño de ventana (escritorio ancho `>= 1120 px`, pantalla dividida `780–1119 px` y modo compacto `< 780 px`):

- **Barra superior:** marca Zenith Audio, selector rápido de **Dispositivo de salida**, selector de **Motor** (`Zenith WASAPI`, `ASIO DSD Nativo`, `BASS`, `MPV`), interruptor **Exclusivo**, botones **Abrir**, **ZenithAI** y **Remoto** (con LED verde cuando el servidor LAN está activo).
- **Columna izquierda (Biblioteca):** navegación 2×2 (`Carpetas`, `DSD`, `Hi-Res PCM`, `Ajustes`), botones `+ Carpeta` / `Música Win`, buscador instantáneo, explorador de pistas y perfil de dispositivo.
- **Escenario central:** título y ruta de la pista, distintivo de transporte, carátula con acento de color dinámico y pestañas centrales (**Escucha** con Medidor VU Zenith + vista previa de letra, **Laboratorio Anti-Fake** y **Letras**).
- **Columna derecha (Inspector de 3 pestañas):** conmutador superior **`Señal`** (bitrate, frecuencia, bits, canales, flujo `Archivo › Decoder › DSP › Salida` y estado bit-perfect), **`Tono / EQ`** (AutoEQ y ecualizador biquad) y **`Ajustes`** (salida, ASIO, DSD, buffer, actualizaciones y créditos).
- **Barra inferior de reproducción:** controles vectoriales Hi-Fi (`Aleatorio`, `Anterior`, `Reproducir`, `Pausar`, `Detener`, `Siguiente`), línea de tiempo deslizable, mini-espectro, volumen y ruta activa.

## 5. Biblioteca masiva y reproducción

1. En el Explorador de biblioteca, pulsa **`+ Carpeta`** para elegir cualquier directorio de música o **`Música Win`** para cargar tu carpeta Música del usuario.
2. El escáner recursivo usa `EnumerationOptions` con `IgnoreInaccessible = true`, omitiendo carpetas protegidas del sistema sin interrumpir bibliotecas de 60 GB o superiores.
3. Tus carpetas se guardan automáticamente en `%LOCALAPPDATA%\ZenithAudio\library.folders.json` y se recargan al iniciar.
4. Usa el buscador para filtrar por título, álbum, carpeta o formato, y haz clic o doble clic para reproducir. Al arrastrar la barra de progreso, Zenith previsualiza el tiempo en vivo y realiza un único salto suave al soltar el botón del mouse.

## 6. Guía completa de configuraciones de la app

La siguiente tabla resume todos los parámetros configurables en Zenith Audio y su valor recomendado según el escenario de escucha:

| Sección | Parámetro | Opciones disponibles | Recomendación práctica |
| --- | --- | --- | --- |
| Barra superior / Ajustes | **Dispositivo de salida** | Salida Windows, Realtek, Headsets USB, DAC USB dedicado | Elige directamente tu DAC USB dedicado cuando escuches Hi-Res o DSD |
| Barra superior / Ajustes | **Motor de audio** | `Zenith WASAPI`, `ASIO (DSD Nativo)`, `BASS WASAPI`, `MPV WASAPI` | `Zenith WASAPI` para uso general; `ASIO (DSD Nativo)` para DACs con driver ASIO (ej. FiiO) |
| Barra superior | **Modo Exclusivo** | `OFF` (por defecto) / `ON` | Actívalo solo en DACs USB dedicados; bloqueado automáticamente en salidas sin driver DAC |
| Ajustes (ASIO) | **Driver ASIO** | Drivers registrados en Windows (`FiiO ASIO Driver`, etc.) | Selecciona el driver oficial de tu DAC y usa **Panel ASIO** para ajustar su buffer |
| Ajustes (DSD) | **Modo DSD** | `PCM FIR fase lineal`, `DoP Exclusivo`, `Solo DSD nativo` | `PCM FIR` para compatibilidad universal; `ASIO` o `DoP` para DACs DSD nativos |
| Ajustes (DSD) | **Ganancia SACD** | `+0 dB`, `+3 dB`, `+6 dB` | `+6 dB` compensa el estándar SACD (-6 dBFS) con limitador suave anti-clipping |
| Ajustes (DSD) | **Frecuencia PCM FIR** | `Auto`, `44.1`, `88.2`, `176.4`, `352.8 kHz` | `Auto` elige el múltiplo exacto más limpio soportado por el endpoint |
| Ajustes (Buffer) | **Buffer de audio** | `40 ms` a `500 ms` (adaptativo) | Hardware WASAPI se mantiene ágil (`45–90 ms`) respaldado por `~550 ms` de pre-lectura en RAM |
| Tono / EQ | **Omitir DSP / EQ** | Bypass bit-transparente o EQ biquad 3 bandas + Preamp | Activa **Omitir DSP** y volumen al 100% para señal bit-perfect |
| Actualizaciones | **GitHub Auto-Update** | Comprobar al iniciar (`ON`), Instalar automático (`OFF`/`ON`) | Mantén **Buscar al iniciar** activo para recibir correcciones y mejoras |
| Control Remoto | **Servidor LAN** | Puerto (`8787`), Red local (`0.0.0.0`) vs Localhost, PIN, Auto-inicio | Deja `0.0.0.0` para controlar desde el teléfono en tu Wi-Fi doméstico |

### Archivos de configuración persistente del usuario

Todas las preferencias se almacenan fuera de `Program Files` para sobrevivir a actualizaciones:

```text
%LOCALAPPDATA%\ZenithAudio\
  ├── audio.settings.json      (salida, motor, driver ASIO, modo/ganancia/frecuencia DSD)
  ├── library.folders.json     (carpetas de música registradas)
  ├── remote.settings.json     (puerto, LAN, PIN y autoarranque del remoto web)
  ├── update.settings.json     (preferencias del actualizador GitHub)
  ├── zenithai.settings.json   (proveedor, endpoint, modelo y clave cifrada con DPAPI)
  └── zenithai.log             (registro de diagnóstico sin claves)
```

## 7. Formatos soportados

| Formato | Extensiones | Ruta de reproducción en Zenith v1.0.8 |
| --- | --- | --- |
| FLAC | `.flac` | Soportado nativamente en WASAPI (Compartido/Exclusivo) y ASIO PCM |
| WAV / PCM | `.wav` | Lectura directa sin pérdida con `WaveFileReader` (16/24/32-bit, multicanal con downmix opcional) |
| AIFF | `.aiff`, `.aif` | Lectura directa con `AiffFileReader` |
| ALAC / Apple Lossless | `.alac`, `.m4a` | Soportado vía Media Foundation / WASAPI / ASIO |
| MP3 / AAC / Ogg / Opus | `.mp3`, `.aac`, `.m4a`, `.ogg`, `.opus` | Soportado vía Media Foundation o backend BASS (`bassopus.dll`) |
| APE / WavPack | `.ape`, `.wv` | Soportado con backend BASS opcional (`bass_ape.dll`, `bass_wv.dll`) |
| DSD DSF | `.dsf` | **ASIO DSD64/DSD128 Nativo**, **DoP v1.1 Exclusivo** o **FIR Blackman-Harris a PCM** |
| DSD DFF (DSDIFF) | `.dff` | ASIO DSD Nativo, DoP o FIR PCM para DFF sin compresión DST (DST requiere BASS/MPV) |
| SACD ISO | `.iso` | Extracción local a DSF mediante `sacd_extract.exe` (herramienta externa opcional) |
| Hojas CUE | `.cue` | Índice virtual de pistas con salto directo al inicio/fin de cada corte |
| MQA | `.mqa`, `.flac` | Reproducible como PCM bit-exacto; el despliegue completo depende del DAC |
| Letras y Carátulas | `.lrc`, `.txt`, `.jpg`, `.png` | Metadata embebida o archivos locales (`cover.*`, `folder.*`, mismo nombre `.lrc`/`.txt`) |

## 8. Salida de audio: WASAPI, Protección DAC y ASIO Nativo

- **Zenith WASAPI Compartido:** ideal para altavoces integrados (Realtek), audífonos Bluetooth o headsets USB; convive con los demás sonidos de Windows usando formato `32-bit float` del mezclador.
- **Zenith WASAPI Exclusivo (Event/Push):** reserva el DAC USB para Zenith Audio evitando el mezclador de Windows. Negocia automáticamente `WAVE_FORMAT_EXTENSIBLE` con subformato `KSDATAFORMAT_SUBTYPE_PCM` (`32-bit` y `24-en-32`), indispensable para controladores USB Audio 2.0 (Thesycon/FiiO/XMOS).
- **Protección contra ruido blanco:** el Modo Exclusivo inicia **desactivado por defecto**. Zenith inspecciona las propiedades reales del dispositivo Windows (`MMDevice`); si la salida no corresponde a un controlador DAC USB dedicado (por ejemplo jack 3.5 mm Realtek o headset gaming), muestra una ventana de alerta y desactiva Exclusivo automáticamente.
- **ASIO (DSD Nativo):** se comunica directamente mediante la interfaz COM `IASIO` (ASIO 2.1+) con el driver oficial del fabricante (ej. `FiiO ASIO Driver` para el **FiiO JadeAudio JA11**), cambiando el hardware a formato `kASIODSDFormat` para enviar DSD64 (2.82 MHz) y DSD128 (5.64 MHz) sin conversión a PCM.

## 9. DSD, FIR Blackman-Harris, DoP v1.1, SACD ISO y Hojas CUE

Zenith Audio ofrece tres caminos de referencia para archivos DSD:

1. **ASIO DSD Nativo:** flujo bit-perfect de 1 bit hacia DACs ASIO compatibles (requiere DSP/EQ en bypass y volumen fijo al 100%).
2. **PCM FIR de fase lineal (Blackman-Harris):** decodificador multietapa propio con tablas LUT de 8 bits (Etapa 1 a 352.8/384 kHz + etapas decimadoras 2× con filtro ultrasónico de 50 kHz), ganancia SACD ajustable (`+0/+3/+6 dB`) y limitador suave *tanh* a partir de `0.94`. Funciona en cualquier salida de Windows.
3. **DoP v1.1 Exclusivo:** encapsula 16 bits DSD junto a los marcadores alternantes `0x05` / `0xFA` en contenedores PCM 24-bit o 24-en-32 a `176.4 kHz` (DSD64) o `352.8 kHz` (DSD128). Bloqueado automáticamente en salidas no-DAC.

Para **SACD ISO**, coloca `sacd_extract.exe` en `src/ZenithAudio/runtimes/win-x64/native/` (o junto al ejecutable instalado). Para **hojas CUE**, mantén el archivo `.cue` en la misma carpeta que el `.flac` o `.wav` referenciado.

## 10. Carátulas, Letras, VU Meter y Laboratorio Anti-Fake

- **Carátulas:** Zenith prioriza la carátula embebida en etiquetas (FLAC/DSF/MP3/M4A) y busca después `cover.jpg/png`, `folder.jpg/png` o `front.jpg/png`. Extrae automáticamente un color dominante para el acento visual OLED (y comparte la carátula con el Control Remoto Web).
- **Letras sincronizadas:** detecta archivos `.lrc` con marcas `[mm:ss.xx]` o `.txt` con el mismo nombre base que la pista, resaltando la línea activa en sincronía con la reproducción.
- **Medidor VU Zenith:** visualiza el nivel RMS real del stream con balística suavizada y mini-espectro en la barra inferior.
- **Laboratorio Anti-Fake:** analiza la pista cargada para estimar autenticidad de alta resolución y detectar posibles archivos reescalados desde formatos con pérdida.

## 11. Control de tono, EQ biquad y AutoEQ

La pestaña **Tono / EQ** implementa procesamiento real en doble precisión (`double`) mediante filtros biquad IIR de 2.º orden:

- **Subgraves:** filtro *Low-Shelf* centrado en `85 Hz` (`S = 0.707`).
- **Presencia:** filtro *Peaking EQ* centrado en `2.8 kHz` (`Q = 0.9`).
- **Aire:** filtro *High-Shelf* centrado en `11 kHz` (limitado automáticamente a `0.42 × Fs`).
- **Preamplificación (`-12` a `+12 dB`):** compensa ganancia positiva para evitar *clipping* digital.
- **Omitir DSP (Bypass):** cuando está activado con volumen al 100%, las muestras cruzan sin ninguna operación matemática (*pass-through* bit-transparente).

## 12. ZenithAI: integración directa y extracción universal de modelos

ZenithAI funciona como panel flotante arrastrable dividido en tres vistas y equipado con un **mini-reproductor sincronizado en vivo** (`Anterior`, `Play/Pausa`, `Siguiente`, codec y motor):

1. **Chat:** conversación experta en audio, música, formatos y control directo del reproductor.
2. **Acciones:** accesos directos de un clic (`ASIO DSD`, `WASAPI`, `Exclusivo`, `Filtrar DSD`, `Filtrar Hi-Res`, `Anti-Fake`, `Panel ASIO`, `Audio puro` y 9 perfiles tonales).
3. **Config API:** menú dedicado independiente del chat con:
   - **11 presets de proveedores:** `NVIDIA NIM`, `OpenAI`, `OpenRouter`, `Groq`, `Google Gemini (OpenAI compat)`, `DeepSeek`, `Mistral AI`, `Together AI`, `Ollama (Local)`, `LM Studio (Local)` y `Personalizado`.
   - **Extracción universal de modelos (`Extraer modelos`):** consulta `/v1/models`, `/models` y `/api/tags` (Ollama), muestra una lista interactiva con buscador instantáneo y permite elegir el modelo con un clic o escribirlo manualmente.
   - **Soporte local sin clave:** `Ollama` y `LM Studio` en `localhost` funcionan sin API key.
   - **Botón `Probar`:** verifica conectividad, credenciales y respuesta real del modelo elegido.

### Comandos directos del reproductor (locales + LLM)

Tanto mediante comandos locales instantáneos (sin gastar tokens) como desde cualquier modelo LLM mediante etiquetas internas `[[ZENITH_ACTION:...]]`, puedes pedirle a ZenithAI:

| Ejemplo de instrucción | Acción ejecutada en Zenith Audio |
| --- | --- |
| `pon Dark Side of the Moon` | Busca la mejor coincidencia en tu biblioteca y la reproduce |
| `pausa` / `reanuda` / `siguiente` / `anterior` | Controla el transporte al instante |
| `volumen 70` / `salta al minuto 2:15` | Ajusta volumen o mueve la posición de la pista |
| `cambia a asio` / `cambia la salida a JadeAudio` | Cambia motor o dispositivo de salida en caliente |
| `filtra dsd` / `filtra hires` / `analiza` | Filtra la biblioteca o ejecuta el análisis Anti-Fake |
| `quiero audio mas puro` / `mas graves` / `voces al frente` | Aplica bypass DSP o perfil biquad con preamp seguro |

## 13. Control Remoto Web (Wi-Fi / LAN)

Zenith Audio incluye un servidor HTTP/1.1 embebido sobre `TcpListener` (no requiere permisos de administrador ni reservas `netsh urlacl` en Windows) para controlar el reproductor desde tu teléfono o tablet en la misma red Wi-Fi:

1. Pulsa el botón **`Remoto`** junto a `ZenithAI` en la barra superior.
2. Pulsa **`Iniciar servidor remoto`** (puerto por defecto `8787`).
3. Si Windows Defender Firewall consulta la primera vez, permite el acceso en **Redes privadas**.
4. Abre la dirección detectada (`http://192.168.x.x:8787`) en el navegador de tu teléfono (puedes usar *"Añadir a pantalla de inicio"* como PWA).

La aplicación web responsiva, estilizada con **Tailwind CSS** en paleta OLED negra + cian, cuenta con tres pestañas en móvil (y vista de 2 columnas en tablet/PC):

- **Ahora:** carátula real en vivo con brillo dinámico, barra VU de 20 segmentos, línea de tiempo deslizable, transporte completo, letra sincronizada y control de volumen (`Mute`, `-5%`, `+5%`, slider, respetando bloqueo bit-perfect en DSD).
- **Canciones:** búsqueda instantánea en servidor sobre toda tu biblioteca cargada con filtros `Todas`, `DSD` y `Hi-Res PCM`.
- **Audio:** cambio en caliente de dispositivo de salida, motor (`WASAPI`, `ASIO DSD`, `BASS`, `MPV`), driver ASIO, Modo Exclusivo (respetando la protección DAC), configuración DSD y control de tono / EQ.

Desde el panel **`Remoto`** en el PC también puedes configurar un **PIN opcional**, cambiar el puerto, restringir a solo `localhost`, ver las IPs conectadas o activar el **inicio automático**.

## 14. Fundamentos Técnicos de Ingeniería de Audio

### 14.1 Pipeline desacoplado de pre-lectura en RAM y latencia

A diferencia de los reproductores básicos que leen disco dentro del hilo de audio, Zenith Audio v1.0.8 utiliza una arquitectura productor-consumidor desacoplada inspirada en **foobar2000**:

- **Hilo productor (`BufferedAudioSampleProvider`, prioridad `AboveNormal`):** decodifica por adelantado **~550 ms** de muestras en un buffer circular en memoria RAM y fusiona peticiones consecutivas de *seek* fuera del hilo de interfaz.
- **Hilo consumidor de hardware (WASAPI / ASIO):** únicamente copia memoria desde RAM (`< 0.05 ms`), manteniéndose aislado de pausas del disco duro o de la latencia de búsqueda de `MediaFoundationReader`.
- **Latencia de endpoint WASAPI (`45–90 ms`):** al delegar la tolerancia a cortes al buffer circular en RAM, el buffer de hardware WASAPI se mantiene corto y ágil, logrando respuestas rápidas al pausar o cambiar de posición.

### 14.2 Eliminación de chasquidos y microcortes al hacer *seek*

Al saltar de un instante a otro de una pista existen tres fuentes clásicas de chasquido eléctrico: saltos bruscos de amplitud (discontinuidad de paso), energía residual en filtros IIR/FIR y saltos de fase en trenes de bits delta-sigma DSD. Zenith Audio resuelve los tres:

- **PCM / WASAPI / ASIO PCM:** `ToneControlSampleProvider.ExecuteSeekSafely` serializa el salto con `Read()`, reinicia las líneas de retardo (`z1`, `z2`) de los biquads y aplica una rampa cosenoidal de bajada (`~5 ms`) seguida de una rampa cosenoidal de entrada (`~14 ms`) cuando llegan las nuevas muestras reales.
- **DSD → PCM FIR:** los registros de desplazamiento del filtro Stage 1 se inicializan y reinician con el byte de silencio DSD **`0x69`** (`01101001`, media `0.0` DC), evitando el impulso a escala completa `-1.0` que produciría rellenar con `0x00`.
- **ASIO DSD Nativo y DoP v1.1:** inserta `~18–28 ms` de bytes `0x69` en el cruce de salto para estabilizar el modulador delta-sigma del DAC a `0 V` sin perder la alternancia de marcadores `0x05 / 0xFA`.

### 14.3 Filtros biquad RBJ y decimación FIR Blackman-Harris

- **Ecualizador biquad:** implementa las ecuaciones normalizadas del *Audio EQ Cookbook* de Robert Bristow-Johnson en Forma Directa II Transpuesta con aritmética `double` de 64 bits para minimizar ruido de cuantización en bajas frecuencias.
- **Decodificador DSD FIR:** utiliza una ventana **Blackman-Harris de 4 términos** (lóbulos laterales por debajo de `-92 dB`) precalculada en tablas de búsqueda de 8 bits (256 combinaciones por etapa) para convertir flujos DSD64–DSD512 a `352.8/384 kHz`, seguido de decimadores FIR 2× en cascada con bucles contiguos sin ramificaciones y filtro ultrasónico de `50 kHz` (recomendación Scarlet Book).

### 14.4 PCM vs DSD Nativo vs DoP v1.1

| Aspecto | PCM (FLAC / WAV) | DSD Nativo (ASIO) | DoP v1.1 (WASAPI Excl.) |
| --- | --- | --- | --- |
| Representación | Muestras multibit (16/24/32-bit) | Flujo 1 bit (`2.82–22.58 MHz`) | Bits DSD intactos en portadora PCM 24-bit |
| Compatibilidad en Windows | Universal (WASAPI / ASIO) | Requiere DAC + driver ASIO DSD | Requiere DAC DoP + WASAPI Exclusivo |
| Control de volumen / EQ digital | Disponible (o bypass bit-perfect) | Fijo al 100% / sin DSP | Fijo al 100% / sin DSP (preserva `0x05/0xFA`) |
| Ruta en Zenith Audio | `WasapiPlaybackEngine` / `AsioPlaybackEngine` | `AsioPlaybackEngine` (`IASIO` COM) | `DsdDoPWaveProvider` (24-bit y 24-en-32) |

### 14.5 Bit-perfect, modo compartido y modo exclusivo

Una ruta es **bit-perfect** cuando las muestras digitales alcanzan el DAC sin remuestreo, mezcla del sistema ni alteración de ganancia. En Windows esto exige:

1. Usar **ASIO Nativo** o **WASAPI Exclusivo** (`AUDCLNT_SHAREMODE_EXCLUSIVE`) con formato exacto (`WAVE_FORMAT_EXTENSIBLE`).
2. Mantener **Omitir DSP** activado y el volumen de software al **100%**.
3. Desactivar mejoras de audio del sistema en las propiedades del dispositivo Windows.

## 15. Solución de problemas

- **No puedo activar Modo Exclusivo (alerta de controlador DAC):** comportamiento de seguridad intencional para salidas integradas (Realtek 3.5 mm, headsets gaming, Bluetooth) que no admiten Exclusive PCM/DoP de forma segura y producirían ruido blanco. Conecta y selecciona tu DAC USB dedicado o cambia al motor **ASIO (DSD Nativo)**.
- **Mi DAC DSD baja a PCM en WASAPI:** muchos DACs USB en Windows (como FiiO / JadeAudio JA11) exponen DSD nativo únicamente vía **ASIO**. Cambia el motor superior a **`ASIO (DSD Nativo)`** y verifica que `Omitir DSP` esté activado.
- **Mi teléfono no carga el Control Remoto Web:** confirma que el botón `Remoto` muestre el punto verde, que el PC y el celular compartan la misma red Wi-Fi, que la opción `Red local (0.0.0.0)` esté activa y que el Firewall de Windows permita `ZenithAudio.exe` en **Redes privadas**.
- **ZenithAI no responde o da error de modelo:** abre **`ZenithAI → Config API`**, selecciona tu proveedor, pulsa **`Extraer modelos`** para escoger un identificador vigente y valida con **`Probar`**. En `Ollama` / `LM Studio`, confirma que el servidor local esté encendido.
- **SACD ISO o DFF con compresión DST no abren:** coloca `sacd_extract.exe` (para ISO) o las DLL de BASS/MPV (para DST) en `runtimes\win-x64\native\`.
- **El actualizador de GitHub no descarga:** verifica tu conexión a internet o descarga directamente el último `.exe` desde [GitHub Releases](https://github.com/xn1llox/Zenith-Audio-Player/releases).

## 16. Donaciones, Créditos y Licencia

Si quieres colaborar con el desarrollo continuo de Zenith Audio Player:

- **Ko-Fi:** [https://ko-fi.com/xn1llox](https://ko-fi.com/xn1llox)
- **BTC:** `bc1qqqwtvasyk2j0jdja6fyhkwg84qm53uwz4935d2`
- **ETH:** `0x0Ce533373C02D5069f193AF0a6e325bdAC8e8F4D`
- **LTC:** `ltc1qmac3zrd49n552c5xjpwm5n2p0d5kyydy5z6fah`

**Desarrollado por:** `xn1llox` — [www.espinozainformatica.cl](https://www.espinozainformatica.cl)  
**Repositorio oficial:** [https://github.com/xn1llox/Zenith-Audio-Player](https://github.com/xn1llox/Zenith-Audio-Player)  
**Licencia:** Apache License 2.0.

## 17. Bibliografía y Referencias Técnicas

1. Microsoft Learn. *"IAudioClient interface (audioclient.h)"*. Documentación oficial de Windows Core Audio APIs / WASAPI. Disponible en: https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iaudioclient
2. Microsoft Learn. *"IAudioClient::Initialize method (audioclient.h)"*. Especificación de inicialización de streams compartidos y exclusivos, buffers y periodicidad en WASAPI. Disponible en: https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-initialize
3. Microsoft Learn. *"AUDCLNT_SHAREMODE enumeration (audiosessiontypes.h)"*. Definición de modos `SHARED` y `EXCLUSIVE` en Windows Audio. Disponible en: https://learn.microsoft.com/en-us/windows/win32/api/audiosessiontypes/ne-audiosessiontypes-audclnt_sharemode
4. Microsoft Learn. *"WAVEFORMATEXTENSIBLE structure (mmreg.h)"*. Formato PCM extensible (`KSDATAFORMAT_SUBTYPE_PCM`, 24-en-32 y 32-bit) para dispositivos USB Audio 2.0 y multicanal. Disponible en: https://learn.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatextensible
5. Microsoft Learn. *"Windows App SDK deployment guide for framework-dependent apps packaged with external location or unpackaged"*. Guía oficial de despliegue para aplicaciones WinUI 3 desempaquetadas. Disponible en: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps
6. Steinberg Media Technologies GmbH. *"ASIO SDK Developer Documentation (ASIO 2.1–2.3, Appendix F: Sony DSD Support)"*. Portal oficial de desarrolladores de Steinberg. Disponible en: https://www.steinberg.net/developers/
7. Koch, Andreas; Reefman, Derk; et al. *"DoP Open Standard: Method for Transferring DSD Audio over PCM Frames (Version 1.1)"*. Especificación abierta de empaquetado DSD sobre PCM (`0x05` / `0xFA`). Disponible en: https://dsd-guide.com/dop-open-standard
8. Bristow-Johnson, Robert (W3C Audio Working Group Note). *"Audio EQ Cookbook"*. Fórmulas estándar de filtros digitales biquad IIR (Low-Shelf, High-Shelf y Peaking EQ). Disponible en: https://www.w3.org/TR/audio-eq-cookbook/
9. Harris, Fredric J. (1978). *"On the Use of Windows for Harmonic Analysis with the Discrete Fourier Transform"*. Proceedings of the IEEE, Vol. 66, No. 1, pp. 51–83 (base matemática de la ventana Blackman-Harris de 4 términos). Disponible en: https://ieeexplore.ieee.org/document/1455106
10. Smith, Julius O. *"Introduction to Digital Filters with Audio Applications"*. CCRMA, Stanford University / W3K Publishing. Disponible en: https://ccrma.stanford.edu/~jos/filters/
11. Smith, Julius O. *"Mathematics of the Discrete Fourier Transform (DFT) with Audio Applications"*. CCRMA, Stanford University / W3K Publishing. Disponible en: https://ccrma.stanford.edu/~jos/mdft/
12. Pohlmann, Ken C. *"Principles of Digital Audio"* (6.ª ed.). McGraw-Hill Education. Referencia clásica de muestreo PCM, cuantización, dither y modulación sigma-delta (SACD/DSD).
13. IETF (Fielding, R., Nottingham, M., Reschke, J.). *"RFC 9110: HTTP Semantics"*. Estándar de protocolo HTTP/1.1 aplicado en el servidor embebido de Control Remoto LAN. Disponible en: https://datatracker.ietf.org/doc/html/rfc9110
14. NVIDIA Corporation. *"NVIDIA NIM for LLMs — OpenAI-Compatible API Reference"*. Documentación de endpoints `/v1/chat/completions` y `/v1/models` usados por ZenithAI. Disponible en: https://docs.api.nvidia.com/nim/reference/llm-apis
15. GitHub Docs. *"REST API endpoints for Releases"*. Especificación de `releases/latest` utilizada por el actualizador automático de Zenith Audio. Disponible en: https://docs.github.com/en/rest/releases/releases
16. Tailwind Labs. *"Tailwind CSS Documentation"*. Framework de utilidades CSS aplicado en la interfaz móvil del Control Remoto Web LAN. Disponible en: https://tailwindcss.com/docs
