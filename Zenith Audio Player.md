"Actúa como un Desarrollador Senior de Software experto en Audio Hi-Res y C#. Necesito crear un reproductor de música Open Source para Windows llamado 'Zenith Audio'. El objetivo es superar a los reproductores comerciales integrando control total del usuario y soporte audiófilo extremo.

Arquitectura Técnica:

Lenguaje: C# con WinUI 3 (Windows App SDK) para una interfaz moderna estilo Windows 11.

Motor de Audio: Utiliza la librería BASS.NET o el backend de MPV para soportar DSD (DSF/DFF/ISO) y WASAPI Exclusive Mode.

Licencia: MIT (Open Source para GitHub).

Funcionalidades Críticas:

Modo Bit-Perfect: Implementar salida WASAPI Exclusive para saltarse el mezclador de Windows.

Ecualizador Paramétrico de 10 y 31 bandas: Debe permitir cargar archivos .txt de configuración.

Base de Datos AutoEQ: Integrar una función para descargar y aplicar perfiles de AutoEQ (como los de Jaakkopasanen) para marcas como Moondrop, KZ (ej. Carol Pro), Sennheiser y Sony.

Gestión de Biblioteca Masiva: Optimizar el escaneo de carpetas para discos de 2TB con miles de archivos DSD/FLAC.

Interfaz de Usuario (UI/UX):

Diseño minimalista con transparencias (Mica material).

Visualización de datos técnicos en tiempo real: Bitrate (kbps), Frecuencia (kHz) y Profundidad de bits (1-bit para DSD).

Panel de control de perfiles de dispositivos precargados.

Aquí tienes la jerarquía completa, desde los más básicos hasta los formatos de ultra-alta fidelidad.1. Formatos con Pérdida (Lossy) - 
Los "Básicos"Estos formatos eliminan información que el oído humano supuestamente no escucha para ahorrar espacio. Son ideales para streaming rápido o cuando te queda poco espacio en el disco de 500 GB.MP3: El estándar universal. A 320 kbps es decente, pero destruye los micro-detalles de los platillos y la profundidad.

AAC (Advanced Audio Coding): El sucesor del MP3. Es el que usa Apple Music y YouTube. Es más eficiente; un AAC a 256 kbps suena mejor que un MP3 a 320 kbps.

OGG Vorbis: El formato que usa Spotify. Es abierto y muy eficiente, pero sigue siendo audio comprimido.2. Formatos Sin Pérdida (Lossless) - Calidad CD Aquí no se elimina nada de información. Es una copia exacta de lo que salió del estudio, pero comprimida matemáticamente (como un archivo .ZIP)

FLAC (Free Lossless Audio Codec): El rey de los audiófilos. No pierde calidad y reduce el peso del archivo a la mitad. Soporta hasta 24-bit/192kHz.

ALAC (Apple Lossless): La versión de Apple del FLAC.WAV: Audio sin comprimir (PCM). Es el formato crudo de Windows. Ocupa mucho espacio porque no tiene compresión de datos, pero la calidad es idéntica al FLAC.

AIFF: El equivalente al WAV, pero desarrollado por Apple. Permite mejores etiquetas de metadatos (carátulas, nombres de artistas). 

Formatos de Alta Resolución (Hi-Res) Los "Mejores"Aquí entramos en el terreno de tu colección. Superan la resolución del CD (16-bit/44.1kHz) para capturar texturas que normalmente se pierden.

MQA (Master Quality Authenticated): Un formato polémico que "empaqueta" audio de alta resolución en archivos pequeños. Se usa mucho en Tidal.

DSD (Direct Stream Digital): El formato de los SACD (Super Audio CD). A diferencia del PCM (WAV/FLAC), el DSD usa un sistema de 1 bit con frecuencias de muestreo altísimas.

DSD64: Calidad estándar de SACD (2.8 MHz).DSD128 / DSD256 / DSD512: Lo que tú tienes. El DSD512 (22.5 MHz) es lo más cercano que existe a la señal analógica original.

DXD (Digital eXtreme Definition): Es básicamente un archivo PCM (como WAV) pero llevado al extremo (24 o 32-bit/352.8kHz). Se usa mucho para editar música que luego se convierte a DSD.

Resumen de Calidad (Bitrate)FormatoBitrate TípicoCategoríaMP3 (Básico)320 kbpsLossy (Con pérdida)CD (WAV/FLAC)1,411 kbpsLossless (Sin pérdida)Hi-Res (24-bit/192kHz)~9,216 kbpsAlta ResoluciónDSD256 (Metallica)~11,290 kbpsUltra-Alta ResoluciónDSD512 (Queen)~22,579 kbpsEl límite actual del audio digital

Tarea Inicial:
Genera la estructura de carpetas del proyecto y el código de la clase principal AudioEngine.cs que inicialice el dispositivo de salida usando WASAPI Exclusive y permita la reproducción de un archivo DSD."


Archivo LICENSE: Elige la MIT License. Es la más libre: cualquiera puede usar tu código, pero tú no eres responsable de lo que ellos hagan.

Archivo README.md: Escribe que es un reproductor diseñado para DSD y Bit-Perfect, mencionando que soporta perfiles de AutoEQ.

El toque "Pro": AutoEQ
Lo que pides de los perfiles precargados es lo más útil. Existe un proyecto llamado AutoEQ en GitHub. Tu app puede "llamar" a esa base de datos. Así, cuando conectes tus KZ Carol Pro, el usuario simplemente busca "KZ" y la app ajusta el ecualizador automáticamente para que suenen perfectos.

Advertencia de hardware: Como vas a manejar archivos de 22 Mbps, asegúrate de que tu código incluya un Buffer ajustable (como el de 100ms que vimos en Auris), o la CPU de tu Samsung/PC sufrirá al procesar tanta información.