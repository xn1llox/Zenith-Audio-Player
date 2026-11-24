namespace ZenithAudio.Core.Remote;

public static class ZenithRemoteWebApp
{
    public const string Html = """
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, viewport-fit=cover">
<meta name="theme-color" content="#040910">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
<link rel="manifest" href="/manifest.json">
<link rel="icon" href="/icon.svg" type="image/svg+xml">
<title>Zenith Audio · Control Remoto</title>
<style>
  :root {
    --bg: #000000;
    --panel: #040910;
    --card: #06101B;
    --card-2: #081523;
    --border: #112C43;
    --border-bright: #1E547B;
    --cyan: #46BFFF;
    --cyan-soft: #7CD5FF;
    --accent: #46BFFF;
    --text: #EAF6FF;
    --muted: #80A5BF;
    --dim: #557891;
    --ok: #38E3A5;
    --warn: #FFB85C;
    --danger: #FF6B7A;
    --radius: 14px;
    --safe-bottom: env(safe-area-inset-bottom, 0px);
  }
  * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; }
  html, body {
    margin: 0; padding: 0;
    background: var(--bg);
    color: var(--text);
    font-family: "Segoe UI", -apple-system, BlinkMacSystemFont, system-ui, sans-serif;
    min-height: 100dvh;
    overflow-x: hidden;
  }
  body {
    background:
      radial-gradient(640px 340px at 50% -80px, color-mix(in srgb, var(--accent) 22%, transparent), transparent 72%),
      #000000;
    padding-bottom: calc(74px + var(--safe-bottom));
  }

  /* Header */
  .topbar {
    position: sticky; top: 0; z-index: 30;
    display: flex; align-items: center; justify-content: space-between; gap: 10px;
    padding: 12px 16px;
    background: rgba(4, 9, 16, 0.9);
    backdrop-filter: blur(14px);
    -webkit-backdrop-filter: blur(14px);
    border-bottom: 1px solid var(--border);
  }
  .brand { display: flex; align-items: center; gap: 10px; min-width: 0; }
  .brand-mark {
    width: 32px; height: 32px; border-radius: 9px; flex-shrink: 0;
    background: linear-gradient(145deg, #0A2740, #030D18);
    border: 1px solid #216694;
    display: grid; place-items: center;
    box-shadow: 0 0 14px rgba(70, 191, 255, 0.22);
  }
  .brand-title { font-weight: 700; font-size: 16px; letter-spacing: .2px; line-height: 1.1; }
  .brand-sub { font-size: 11px; color: var(--muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 190px; }
  .live-pill {
    display: inline-flex; align-items: center; gap: 6px;
    padding: 5px 10px; border-radius: 999px;
    background: #061524; border: 1px solid #163E5C;
    font-size: 11px; font-weight: 600; color: #B4E2FF; flex-shrink: 0;
  }
  .live-dot {
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--ok);
    box-shadow: 0 0 8px var(--ok);
    animation: pulse 2s infinite;
  }
  .live-pill.offline .live-dot { background: var(--danger); box-shadow: 0 0 8px var(--danger); animation: none; }
  .live-pill.offline { border-color: #5C2230; color: #FFB3BC; }
  @keyframes pulse { 0%,100% { opacity: 1; } 50% { opacity: .45; } }

  /* Toast */
  .toast {
    position: fixed; top: 64px; left: 50%; transform: translateX(-50%) translateY(-16px);
    z-index: 60; pointer-events: none; opacity: 0;
    transition: opacity .22s ease, transform .22s ease;
    background: #072238; border: 1px solid var(--cyan);
    color: #EAF7FF; padding: 9px 14px; border-radius: 10px;
    font-size: 12px; font-weight: 600; max-width: calc(100vw - 32px);
    box-shadow: 0 10px 28px rgba(0,0,0,.65);
    text-align: center;
  }
  .toast.show { opacity: 1; transform: translateX(-50%) translateY(0); }
  .toast.error { background: #2D0C14; border-color: #FF6B7A; color: #FFD7DC; }

  /* Main layout */
  .shell {
    max-width: 1060px; margin: 0 auto;
    padding: 14px 14px 18px;
    display: grid; gap: 14px;
  }
  .view { display: none; flex-direction: column; gap: 14px; }
  .view.active { display: flex; }

  @media (min-width: 860px) {
    body { padding-bottom: 24px; }
    .shell { grid-template-columns: 390px 1fr; align-items: start; padding: 18px; }
    #view-now { display: flex !important; position: sticky; top: 72px; }
    .desktop-right { display: flex; flex-direction: column; gap: 14px; }
    .desktop-tabs { display: flex !important; }
    .bottom-nav { display: none !important; }
  }

  .desktop-tabs {
    display: none; gap: 8px;
    background: var(--panel); border: 1px solid var(--border);
    padding: 5px; border-radius: 11px;
  }
  .desktop-tabs button {
    flex: 1; padding: 8px 12px; border-radius: 8px;
    border: 1px solid transparent; background: transparent;
    color: var(--muted); font-size: 13px; font-weight: 600; cursor: pointer;
  }
  .desktop-tabs button.active {
    background: #082B44; border-color: #2986C4; color: #EAF7FF;
  }

  .card {
    background: linear-gradient(180deg, rgba(6,16,28,0.96), rgba(4,9,16,0.98));
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 15px;
  }
  .section-kicker {
    font-size: 11px; font-weight: 700; letter-spacing: 1px;
    text-transform: uppercase; color: #6799B8; margin-bottom: 10px;
    display: flex; align-items: center; justify-content: space-between;
  }

  /* Now Playing Card */
  .cover-wrap {
    position: relative; width: 100%; max-width: 268px; aspect-ratio: 1 / 1;
    margin: 2px auto 14px; border-radius: 16px; overflow: hidden;
    background: #030A13; border: 1px solid #163C59;
    box-shadow: 0 18px 42px rgba(0, 0, 0, .75), 0 0 38px color-mix(in srgb, var(--accent) 26%, transparent);
  }
  .cover-wrap img {
    width: 100%; height: 100%; object-fit: cover; display: block;
  }
  .cover-badge {
    position: absolute; top: 10px; right: 10px;
    padding: 4px 9px; border-radius: 999px;
    background: rgba(3, 12, 22, 0.85); border: 1px solid rgba(70, 191, 255, 0.45);
    color: #9EE0FF; font-size: 10px; font-weight: 700; letter-spacing: .6px;
    backdrop-filter: blur(8px);
  }

  .track-title {
    font-size: 19px; font-weight: 700; line-height: 1.24;
    text-align: center; color: #F2FAFF;
    word-break: break-word;
  }
  .track-sub {
    margin-top: 4px; font-size: 12px; color: var(--muted);
    text-align: center; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .lyric-line {
    margin-top: 8px; min-height: 18px;
    font-size: 12px; color: var(--cyan-soft); text-align: center;
    font-style: italic; opacity: .92;
  }

  .meta-pills {
    display: flex; flex-wrap: wrap; justify-content: center; gap: 6px;
    margin-top: 12px;
  }
  .pill {
    padding: 4px 9px; border-radius: 999px;
    background: #061423; border: 1px solid #143652;
    font-size: 11px; font-weight: 600; color: #9ECAE6;
  }
  .pill.hi { background: #082942; border-color: #2682BD; color: #D8F3FF; }

  /* Live mini VU strip */
  .vu-strip {
    display: grid; grid-template-columns: repeat(16, 1fr); gap: 3px;
    height: 18px; align-items: end; margin: 12px 0 8px; padding: 0 6px;
  }
  .vu-bar {
    height: 3px; border-radius: 2px;
    background: linear-gradient(180deg, #7EE0FF, #289CE0);
    transition: height .14s ease-out, opacity .14s ease-out;
    opacity: .35;
  }

  /* Timeline */
  .timeline { margin-top: 4px; }
  .time-row {
    display: flex; justify-content: space-between;
    font-size: 11px; font-variant-numeric: tabular-nums;
    color: var(--muted); margin-top: 4px;
  }

  /* Range sliders */
  input[type=range] {
    -webkit-appearance: none; appearance: none;
    width: 100%; height: 28px; background: transparent; cursor: pointer; margin: 0;
  }
  input[type=range]::-webkit-slider-runnable-track {
    height: 6px; border-radius: 999px;
    background: linear-gradient(90deg, var(--cyan) var(--val, 0%), #102A40 var(--val, 0%));
  }
  input[type=range]::-webkit-slider-thumb {
    -webkit-appearance: none; appearance: none;
    width: 18px; height: 18px; border-radius: 50%; margin-top: -6px;
    background: #E8F8FF; border: 2px solid var(--cyan);
    box-shadow: 0 0 10px rgba(70,191,255,.55);
  }
  input[type=range]:disabled { opacity: .45; cursor: not-allowed; }

  /* Transport controls */
  .transport {
    display: flex; align-items: center; justify-content: center; gap: 10px;
    margin-top: 12px;
  }
  .t-btn {
    width: 46px; height: 46px; border-radius: 50%;
    border: 1px solid #163C5A; background: #071524; color: #D7F0FF;
    display: grid; place-items: center; cursor: pointer;
    transition: transform .12s ease, background .15s ease, border-color .15s ease;
  }
  .t-btn:active { transform: scale(.93); }
  .t-btn.active { background: #0B3A5C; border-color: var(--cyan); color: #FFFFFF; box-shadow: 0 0 14px rgba(70,191,255,.28); }
  .t-btn.primary {
    width: 60px; height: 60px;
    background: linear-gradient(145deg, #1678B8, #0A4670);
    border-color: #54C7FF; color: #FFFFFF;
    box-shadow: 0 0 24px rgba(70,191,255,.35);
  }
  .t-btn svg { width: 20px; height: 20px; fill: currentColor; }
  .t-btn.primary svg { width: 24px; height: 24px; }

  /* Volume card */
  .vol-box {
    margin-top: 14px; padding-top: 12px;
    border-top: 1px solid #0E2538;
  }
  .vol-head {
    display: flex; align-items: center; justify-content: space-between;
    margin-bottom: 6px; font-size: 12px;
  }
  .vol-row { display: flex; align-items: center; gap: 8px; }
  .mini-btn {
    min-width: 36px; height: 34px; padding: 0 10px; border-radius: 9px;
    border: 1px solid #163C5A; background: #071524; color: #D5EEFF;
    font-size: 12px; font-weight: 700; cursor: pointer;
    display: inline-flex; align-items: center; justify-content: center; gap: 5px;
  }
  .mini-btn:active { transform: scale(.95); }
  .mini-btn.active { background: #0A3E63; border-color: var(--cyan); color: #FFFFFF; }

  /* Library */
  .search-wrap {
    display: flex; gap: 8px; align-items: center;
  }
  .search-input, .select-box {
    width: 100%; padding: 11px 13px; border-radius: 10px;
    background: #030A12; border: 1px solid #153854;
    color: var(--text); font-size: 14px; outline: none;
  }
  .search-input:focus, .select-box:focus { border-color: var(--cyan); }
  .chips { display: flex; gap: 7px; flex-wrap: wrap; margin-top: 10px; }
  .chip {
    padding: 7px 12px; border-radius: 999px;
    border: 1px solid #153854; background: #061320; color: var(--muted);
    font-size: 12px; font-weight: 600; cursor: pointer;
  }
  .chip.active { background: #093554; border-color: var(--cyan); color: #E8F8FF; }

  .track-list {
    display: flex; flex-direction: column; gap: 6px; margin-top: 12px;
  }
  .track-item {
    display: grid; grid-template-columns: 1fr auto; gap: 10px; align-items: center;
    padding: 11px 12px; border-radius: 11px;
    background: #050E18; border: 1px solid #0E2538;
    cursor: pointer; text-align: left; width: 100%; color: inherit;
    transition: border-color .15s, background .15s;
  }
  .track-item:active { transform: scale(.99); }
  .track-item.current {
    background: #08243A; border-color: var(--cyan);
    box-shadow: inset 3px 0 0 var(--cyan);
  }
  .track-item-title {
    font-size: 14px; font-weight: 600; color: #EBF7FF;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .track-item-folder {
    font-size: 11px; color: var(--muted); margin-top: 2px;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .fmt-badge {
    padding: 3px 8px; border-radius: 7px; font-size: 10px; font-weight: 700;
    background: #072035; border: 1px solid #1C557D; color: #8DD7FF;
    text-transform: uppercase;
  }
  .fmt-badge.dsd { background: #0A314F; border-color: #39B4FA; color: #D8F5FF; }

  /* Audio Controls */
  .seg-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; }
  .seg-btn {
    padding: 10px 10px; border-radius: 10px;
    background: #05101B; border: 1px solid #143652;
    color: var(--muted); font-size: 12px; font-weight: 600; cursor: pointer;
    text-align: center;
  }
  .seg-btn.active { background: #093556; border-color: var(--cyan); color: #F0FAFF; }

  .device-list { display: flex; flex-direction: column; gap: 7px; }
  .device-btn {
    display: flex; align-items: center; justify-content: space-between; gap: 10px;
    width: 100%; padding: 11px 12px; border-radius: 10px;
    background: #050F1A; border: 1px solid #12314A; color: var(--text);
    cursor: pointer; text-align: left;
  }
  .device-btn.active { background: #082B44; border-color: var(--cyan); }
  .device-name { font-size: 13px; font-weight: 600; }
  .device-tag {
    font-size: 10px; font-weight: 700; padding: 3px 8px; border-radius: 999px;
    background: #071D30; border: 1px solid #18496B; color: #8BD4FF; flex-shrink: 0;
  }
  .device-tag.dac { background: #0B3A54; border-color: #3BC0FF; color: #E2F7FF; }

  .row-between {
    display: flex; align-items: center; justify-content: space-between; gap: 12px;
  }
  .switch {
    position: relative; width: 48px; height: 28px; border-radius: 999px;
    background: #102436; border: 1px solid #1E4766; cursor: pointer;
    transition: background .18s, border-color .18s; flex-shrink: 0;
  }
  .switch::after {
    content: ""; position: absolute; top: 3px; left: 3px;
    width: 20px; height: 20px; border-radius: 50%; background: #9BC0D9;
    transition: transform .18s, background .18s;
  }
  .switch.on { background: #0E679E; border-color: #54CAFF; }
  .switch.on::after { transform: translateX(20px); background: #FFFFFF; }

  .field-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 10px; }
  .field label { display: block; font-size: 11px; color: var(--muted); margin-bottom: 5px; font-weight: 600; }
  .eq-row { margin-top: 8px; }
  .eq-row-head { display: flex; justify-content: space-between; font-size: 12px; color: #B9D8EC; margin-bottom: 2px; }

  /* Bottom Nav */
  .bottom-nav {
    position: fixed; left: 0; right: 0; bottom: 0; z-index: 40;
    display: grid; grid-template-columns: repeat(3, 1fr);
    background: rgba(4, 9, 16, 0.94);
    backdrop-filter: blur(14px);
    -webkit-backdrop-filter: blur(14px);
    border-top: 1px solid var(--border);
    padding: 6px 8px calc(6px + var(--safe-bottom));
  }
  .nav-btn {
    display: flex; flex-direction: column; align-items: center; gap: 3px;
    background: transparent; border: none; color: var(--muted);
    padding: 6px 4px; border-radius: 10px; cursor: pointer;
    font-size: 11px; font-weight: 600;
  }
  .nav-btn svg { width: 20px; height: 20px; fill: currentColor; }
  .nav-btn.active { color: var(--cyan); background: rgba(70, 191, 255, 0.09); }

  /* PIN Modal */
  .pin-overlay {
    position: fixed; inset: 0; z-index: 80; display: none; place-items: center;
    background: rgba(0,0,0,.86); backdrop-filter: blur(10px); padding: 20px;
  }
  .pin-overlay.show { display: grid; }
  .pin-card {
    width: 100%; max-width: 340px;
    background: #040B14; border: 1px solid #1E5A84; border-radius: 16px;
    padding: 20px; display: flex; flex-direction: column; gap: 12px; text-align: center;
  }
  .btn-primary {
    padding: 11px 16px; border-radius: 10px; border: 1px solid #48C0FF;
    background: #0A4D7A; color: #F0FAFF; font-size: 14px; font-weight: 700; cursor: pointer;
  }
  .btn-ghost {
    padding: 10px 14px; border-radius: 10px; border: 1px solid #183F5E;
    background: #061422; color: #B9DDF5; font-size: 13px; font-weight: 600; cursor: pointer;
  }
  .empty-state {
    text-align: center; padding: 26px 12px; color: var(--muted); font-size: 13px;
  }
</style>
</head>
<body>
  <header class="topbar">
    <div class="brand">
      <div class="brand-mark">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none">
          <path d="M5 6h14L9 18h10" stroke="#58C8FF" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </div>
      <div style="min-width:0">
        <div class="brand-title">Zenith Audio</div>
        <div class="brand-sub" id="hostSub">Control remoto LAN</div>
      </div>
    </div>
    <div class="live-pill" id="livePill">
      <span class="live-dot"></span>
      <span id="liveLabel">En vivo</span>
    </div>
  </header>

  <div class="toast" id="toast"></div>

  <main class="shell">
    <!-- NOW PLAYING VIEW -->
    <section id="view-now" class="view active">
      <div class="card">
        <div class="cover-wrap">
          <img id="coverImg" src="/icon.svg" alt="Carátula">
          <span class="cover-badge" id="transportBadge">LISTO</span>
        </div>

        <div class="track-title" id="trackTitle">Sin pista cargada</div>
        <div class="track-sub" id="trackSubtitle">Elige una canción desde Biblioteca o en el PC</div>
        <div class="lyric-line" id="lyricLine"></div>

        <div class="meta-pills">
          <span class="pill hi" id="codecPill">PCM</span>
          <span class="pill" id="ratePill">-- kHz</span>
          <span class="pill" id="enginePill">WASAPI</span>
          <span class="pill" id="outputPill">Salida</span>
        </div>

        <div class="vu-strip" id="vuStrip"></div>

        <div class="timeline">
          <input type="range" id="seekSlider" min="0" max="100" step="0.5" value="0">
          <div class="time-row">
            <span id="curTime">0:00</span>
            <span id="durTime">0:00</span>
          </div>
        </div>

        <div class="transport">
          <button class="t-btn" id="btnShuffle" title="Aleatorio" onclick="sendAction('shuffle')">
            <svg viewBox="0 0 24 24"><path d="M16 3h5v5h-2V6.41l-4.29 4.3-1.42-1.42L17.59 5H16V3zM4 6h4.59l9.7 9.71L19.59 17H16v2h5v-5h-2v1.59l-9.71-9.7L8.59 5H4v1zm0 11h4.59l3.12-3.12 1.41 1.41L9.41 19H4v-2z"/></svg>
          </button>
          <button class="t-btn" title="Anterior" onclick="sendAction('previous')">
            <svg viewBox="0 0 24 24"><path d="M6 6h2v12H6V6zm3.5 6 8.5 6V6l-8.5 6z"/></svg>
          </button>
          <button class="t-btn primary" id="btnPlayPause" title="Reproducir / Pausar" onclick="sendAction('toggle_play')">
            <svg id="playPauseIcon" viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>
          </button>
          <button class="t-btn" title="Detener" onclick="sendAction('stop')">
            <svg viewBox="0 0 24 24"><rect x="6" y="6" width="12" height="12" rx="2"/></svg>
          </button>
          <button class="t-btn" title="Siguiente" onclick="sendAction('next')">
            <svg viewBox="0 0 24 24"><path d="M6 18l8.5-6L6 6v12zm10-12v12h2V6h-2z"/></svg>
          </button>
        </div>

        <div class="vol-box">
          <div class="vol-head">
            <span style="color:#8CB2CC;font-weight:600">Volumen</span>
            <span id="volLabel" style="color:#DFF4FF;font-weight:700">100%</span>
          </div>
          <div class="vol-row">
            <button class="mini-btn" id="btnMute" onclick="toggleMute()">M</button>
            <button class="mini-btn" onclick="nudgeVolume(-5)">-5</button>
            <input type="range" id="volSlider" min="0" max="100" step="1" value="100">
            <button class="mini-btn" onclick="nudgeVolume(5)">+5</button>
          </div>
        </div>
      </div>
    </section>

    <div class="desktop-right">
      <div class="desktop-tabs">
        <button id="dtab-library" class="active" onclick="switchTab('library')">Canciones</button>
        <button id="dtab-audio" onclick="switchTab('audio')">Salida, Motor y EQ</button>
      </div>

      <!-- LIBRARY VIEW -->
      <section id="view-library" class="view">
        <div class="card">
          <div class="section-kicker">
            <span>Biblioteca cargada</span>
            <span id="libCounter" style="color:#9FD6F7">0 pistas</span>
          </div>
          <div class="search-wrap">
            <input id="libSearch" class="search-input" type="search" placeholder="Buscar canción, álbum o formato..." oninput="onSearchInput()">
            <button class="mini-btn" onclick="clearSearch()" title="Limpiar">✕</button>
          </div>
          <div class="chips">
            <button class="chip active" id="flt-all" onclick="setFilter('all')">Todas</button>
            <button class="chip" id="flt-dsd" onclick="setFilter('dsd')">DSD</button>
            <button class="chip" id="flt-hires" onclick="setFilter('hires')">Hi-Res PCM</button>
          </div>
          <div class="track-list" id="trackList"></div>
          <div id="loadMoreWrap" style="margin-top:12px;display:none;text-align:center">
            <button class="btn-ghost" onclick="loadLibrary(true)">Cargar más pistas</button>
          </div>
        </div>
      </section>

      <!-- AUDIO / OUTPUT / DSD / EQ VIEW -->
      <section id="view-audio" class="view">
        <div class="card">
          <div class="section-kicker">
            <span>Salida de audio</span>
            <button class="mini-btn" style="height:26px;font-size:11px" onclick="sendAction('refresh_devices')">Actualizar</button>
          </div>
          <div class="device-list" id="deviceList"></div>
        </div>

        <div class="card">
          <div class="section-kicker"><span>Motor y Modo Exclusivo</span></div>
          <div class="seg-grid" id="backendGrid"></div>

          <div id="asioDriverWrap" style="margin-top:12px;display:none">
            <div class="field">
              <label>Driver ASIO activo</label>
              <select id="asioSelect" class="select-box" onchange="onAsioDriverChange()"></select>
            </div>
          </div>

          <div class="row-between" style="margin-top:14px;padding-top:12px;border-top:1px solid #0E2538">
            <div>
              <div style="font-size:13px;font-weight:700">Modo Exclusivo WASAPI</div>
              <div id="dacHint" style="font-size:11px;color:var(--muted);margin-top:2px">Protección DAC activa</div>
            </div>
            <button class="switch" id="exclusiveSwitch" onclick="toggleExclusive()" aria-label="Modo Exclusivo"></button>
          </div>
        </div>

        <div class="card">
          <div class="section-kicker"><span>Configuración DSD</span></div>
          <div class="field">
            <label>Modo de salida DSD</label>
            <select id="dsdModeSelect" class="select-box" onchange="sendDsdConfig()">
              <option value="0">PCM FIR de fase lineal (universal)</option>
              <option value="1">DoP Exclusivo (DSD sobre PCM)</option>
              <option value="2">Solo DSD nativo (ASIO / BASS / MPV)</option>
            </select>
          </div>
          <div class="field-grid">
            <div class="field">
              <label>Ganancia SACD</label>
              <select id="dsdGainSelect" class="select-box" onchange="sendDsdConfig()">
                <option value="0">+0 dB (Referencia)</option>
                <option value="3">+3 dB</option>
                <option value="6">+6 dB (Estándar)</option>
              </select>
            </div>
            <div class="field">
              <label>Frecuencia PCM FIR</label>
              <select id="dsdRateSelect" class="select-box" onchange="sendDsdConfig()">
                <option value="0">Automática</option>
                <option value="44100">44.1 kHz</option>
                <option value="88200">88.2 kHz</option>
                <option value="176400">176.4 kHz</option>
                <option value="352800">352.8 kHz</option>
              </select>
            </div>
          </div>
        </div>

        <div class="card">
          <div class="section-kicker">
            <span>Control de tono / EQ</span>
            <span id="dspStateLabel" style="color:#8ED6FF">Bypass</span>
          </div>
          <div class="chips" style="margin-top:0;margin-bottom:10px">
            <button class="chip" onclick="applyTonePreset('puro')">Puro</button>
            <button class="chip" onclick="applyTonePreset('calido')">Cálido</button>
            <button class="chip" onclick="applyTonePreset('voces')">Voces</button>
            <button class="chip" onclick="applyTonePreset('aireado')">Aireado</button>
            <button class="chip" onclick="applyTonePreset('estudio')">Estudio</button>
          </div>
          <div class="row-between" style="margin-bottom:8px">
            <span style="font-size:13px;font-weight:600">Bypass bit-transparente</span>
            <button class="switch" id="bypassSwitch" onclick="toggleBypass()"></button>
          </div>
          <div class="row-between" style="margin-bottom:10px">
            <span style="font-size:13px;font-weight:600">Ecualizador biquad activo</span>
            <button class="switch" id="eqSwitch" onclick="toggleEq()"></button>
          </div>

          <div class="eq-row">
            <div class="eq-row-head"><span>Preamp</span><span id="valPreamp">0.0 dB</span></div>
            <input type="range" id="eqPreamp" min="-12" max="12" step="0.5" value="0">
          </div>
          <div class="eq-row">
            <div class="eq-row-head"><span>Sub-graves (85 Hz)</span><span id="valSub">0.0 dB</span></div>
            <input type="range" id="eqSub" min="-6" max="6" step="0.5" value="0">
          </div>
          <div class="eq-row">
            <div class="eq-row-head"><span>Presencia (2.8 kHz)</span><span id="valPres">0.0 dB</span></div>
            <input type="range" id="eqPres" min="-6" max="6" step="0.5" value="0">
          </div>
          <div class="eq-row">
            <div class="eq-row-head"><span>Aire (11 kHz)</span><span id="valAir">0.0 dB</span></div>
            <input type="range" id="eqAir" min="-6" max="6" step="0.5" value="0">
          </div>
        </div>
      </section>
    </div>
  </main>

  <nav class="bottom-nav">
    <button class="nav-btn active" id="nav-now" onclick="switchTab('now')">
      <svg viewBox="0 0 24 24"><path d="M12 3v10.55A4 4 0 1 0 14 17V7h4V3h-6z"/></svg>
      <span>Ahora</span>
    </button>
    <button class="nav-btn" id="nav-library" onclick="switchTab('library')">
      <svg viewBox="0 0 24 24"><path d="M4 6h16v2H4V6zm0 5h16v2H4v-2zm0 5h10v2H4v-2zm13 0v5l4-2.5L17 16z"/></svg>
      <span>Canciones</span>
    </button>
    <button class="nav-btn" id="nav-audio" onclick="switchTab('audio')">
      <svg viewBox="0 0 24 24"><path d="M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z"/></svg>
      <span>Audio</span>
    </button>
  </nav>

  <div class="pin-overlay" id="pinOverlay">
    <div class="pin-card">
      <div style="font-size:18px;font-weight:700">Zenith Audio Remoto</div>
      <div style="font-size:12px;color:var(--muted)">Ingresa el PIN configurado en Zenith Audio para controlar el reproductor.</div>
      <input id="pinInput" class="search-input" type="password" inputmode="numeric" placeholder="PIN de acceso" onkeydown="if(event.key==='Enter')submitPin()">
      <button class="btn-primary" onclick="submitPin()">Desbloquear</button>
    </div>
  </div>

<script>
  let state = null;
  let activeTab = window.innerWidth >= 860 ? 'library' : 'now';
  let libFilter = 'all';
  let libOffset = 0;
  let libItems = [];
  let lastCoverVersion = -1;
  let lastLibraryTotal = -1;
  let lastDevicesSig = '';
  let lastBackendsSig = '';
  let lastAsioSig = '';
  let draggingSeek = false;
  let draggingVol = false;
  let draggingEq = false;
  let lastNonZeroVolume = 80;
  let searchTimer = null;
  let volTimer = null;
  let eqTimer = null;
  let toastTimer = null;
  let pin = localStorage.getItem('zenith_remote_pin') || '';

  const vuStrip = document.getElementById('vuStrip');
  for (let i = 0; i < 16; i++) {
    const b = document.createElement('div');
    b.className = 'vu-bar';
    vuStrip.appendChild(b);
  }

  function authHeaders(extra = {}) {
    const h = Object.assign({}, extra);
    if (pin) h['X-Zenith-Pin'] = pin;
    return h;
  }

  function showToast(msg, isError = false) {
    const el = document.getElementById('toast');
    el.textContent = msg;
    el.className = 'toast show' + (isError ? ' error' : '');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { el.className = 'toast'; }, 2400);
  }

  function setSliderFill(el) {
    const min = parseFloat(el.min || '0');
    const max = parseFloat(el.max || '100');
    const val = parseFloat(el.value || '0');
    const pct = max > min ? ((val - min) * 100) / (max - min) : 0;
    el.style.setProperty('--val', pct.toFixed(1) + '%');
  }

  function fmtTime(sec) {
    sec = Math.max(0, Math.floor(sec || 0));
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    return m + ':' + String(s).padStart(2, '0');
  }

  function fmtDb(v) {
    const n = Number(v || 0);
    return (n > 0 ? '+' : '') + n.toFixed(1) + ' dB';
  }

  function switchTab(tab) {
    activeTab = tab;
    const isDesktop = window.innerWidth >= 860;
    if (isDesktop && tab === 'now') tab = 'library';

    ['now', 'library', 'audio'].forEach(t => {
      const v = document.getElementById('view-' + t);
      if (v) v.classList.toggle('active', t === tab);
      const nb = document.getElementById('nav-' + t);
      if (nb) nb.classList.toggle('active', t === tab);
      const db = document.getElementById('dtab-' + t);
      if (db) db.classList.toggle('active', t === tab);
    });

    if (tab === 'library' && libItems.length === 0) {
      loadLibrary(false);
    }
  }

  window.addEventListener('resize', () => {
    if (window.innerWidth >= 860 && activeTab === 'now') switchTab('library');
  });

  async function apiFetch(url, options = {}) {
    options.headers = authHeaders(options.headers || {});
    const res = await fetch(url, options);
    if (res.status === 401) {
      document.getElementById('pinOverlay').classList.add('show');
      throw new Error('PIN requerido');
    }
    document.getElementById('pinOverlay').classList.remove('show');
    return res;
  }

  async function submitPin() {
    pin = document.getElementById('pinInput').value.trim();
    localStorage.setItem('zenith_remote_pin', pin);
    try {
      await pollState();
      loadLibrary(false);
      showToast('Acceso concedido');
    } catch {
      showToast('PIN incorrecto', true);
    }
  }

  async function sendAction(action, extra = {}, silent = false) {
    try {
      const res = await apiFetch('/api/action', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(Object.assign({ action }, extra))
      });
      const data = await res.json();
      if (!data.ok) {
        showToast(data.message || 'Acción rechazada', true);
      } else if (!silent && data.message) {
        showToast(data.message);
      }
      await pollState();
      return data;
    } catch (e) {
      if (e.message !== 'PIN requerido') showToast('Sin conexión con Zenith Audio', true);
      return null;
    }
  }

  async function pollState() {
    const pill = document.getElementById('livePill');
    const lbl = document.getElementById('liveLabel');
    try {
      const res = await apiFetch('/api/state');
      const s = await res.json();
      state = s;
      pill.classList.remove('offline');
      lbl.textContent = s.isPlaying && !s.isPaused ? 'En vivo' : 'Conectado';
      renderState(s);
    } catch (e) {
      if (e.message !== 'PIN requerido') {
        pill.classList.add('offline');
        lbl.textContent = 'Reconectando';
      }
    }
  }

  function renderState(s) {
    if (s.accentHex) document.documentElement.style.setProperty('--accent', s.accentHex);
    document.getElementById('hostSub').textContent = s.serverHostName ? (s.serverHostName + ' · LAN') : 'Control remoto LAN';
    document.getElementById('trackTitle').textContent = s.trackTitle || 'Sin pista cargada';
    document.getElementById('trackSubtitle').textContent = s.trackSubtitle || 'Selecciona una canción';
    document.getElementById('lyricLine').textContent = s.activeLyricLine || '';
    document.getElementById('transportBadge').textContent = s.transportBadge || 'LISTO';

    document.getElementById('codecPill').textContent = s.codec || 'PCM';
    document.getElementById('ratePill').textContent = [s.sampleRate, s.bitDepth].filter(Boolean).join(' · ') || '--';
    document.getElementById('enginePill').textContent = s.backendName || 'WASAPI';
    document.getElementById('outputPill').textContent = s.outputPath || 'Salida';

    if (s.coverVersion !== lastCoverVersion) {
      lastCoverVersion = s.coverVersion;
      const pinQuery = pin ? '&pin=' + encodeURIComponent(pin) : '';
      document.getElementById('coverImg').src = '/api/cover?v=' + s.coverVersion + pinQuery;
    }

    // Play / Pause icon
    const icon = document.getElementById('playPauseIcon');
    if (s.isPlaying && !s.isPaused) {
      icon.innerHTML = '<rect x="6" y="5" width="4" height="14" rx="1.2"/><rect x="14" y="5" width="4" height="14" rx="1.2"/>';
    } else {
      icon.innerHTML = '<path d="M8 5v14l11-7z"/>';
    }
    document.getElementById('btnShuffle').classList.toggle('active', !!s.shuffle);

    // VU strip
    const bars = vuStrip.children;
    const lvl = s.isPlaying && !s.isPaused ? Math.max(0.06, Math.min(1, s.vuLevel || 0)) : 0;
    for (let i = 0; i < bars.length; i++) {
      const centerWeight = 1 - Math.abs(i - 7.5) / 9;
      const jitter = lvl > 0 ? (0.55 + Math.random() * 0.45) : 0;
      const h = lvl > 0 ? Math.max(3, Math.round(18 * lvl * centerWeight * jitter)) : 3;
      bars[i].style.height = h + 'px';
      bars[i].style.opacity = lvl > 0 ? (0.45 + 0.55 * centerWeight) : 0.2;
    }

    // Seek
    const seek = document.getElementById('seekSlider');
    const maxDur = Math.max(1, s.durationSeconds || 1);
    if (!draggingSeek) {
      seek.max = maxDur;
      seek.value = Math.min(maxDur, Math.max(0, s.positionSeconds || 0));
      setSliderFill(seek);
      document.getElementById('curTime').textContent = s.currentTimeText || fmtTime(s.positionSeconds);
    }
    document.getElementById('durTime').textContent = s.durationText || fmtTime(s.durationSeconds);

    // Volume
    const vol = document.getElementById('volSlider');
    vol.disabled = !!s.volumeLocked;
    if (!draggingVol) {
      vol.value = Math.round(s.volume ?? 100);
      setSliderFill(vol);
      if ((s.volume ?? 0) > 0) lastNonZeroVolume = Math.round(s.volume);
    }
    document.getElementById('volLabel').textContent = s.volumeLabel || (Math.round(s.volume ?? 100) + '%');
    document.getElementById('btnMute').classList.toggle('active', (s.volume ?? 100) <= 0);

    // Library counts
    document.getElementById('flt-all').textContent = 'Todas (' + (s.totalLibraryTracks || 0) + ')';
    document.getElementById('flt-dsd').textContent = 'DSD (' + (s.dsdTracksCount || 0) + ')';
    document.getElementById('flt-hires').textContent = 'Hi-Res (' + (s.hiResTracksCount || 0) + ')';
    if (s.totalLibraryTracks !== lastLibraryTotal) {
      lastLibraryTotal = s.totalLibraryTracks;
      if (activeTab === 'library' || window.innerWidth >= 860) loadLibrary(false);
    }
    highlightCurrentTrack(s.trackPath);

    // Audio devices (only rebuild DOM when list/selection changes to keep mobile taps smooth)
    const devicesSig = JSON.stringify(s.outputDevices || []);
    if (devicesSig !== lastDevicesSig) {
      lastDevicesSig = devicesSig;
      const devList = document.getElementById('deviceList');
      devList.innerHTML = '';
      (s.outputDevices || []).forEach(d => {
        const btn = document.createElement('button');
        btn.className = 'device-btn' + (d.isSelected ? ' active' : '');
        btn.onclick = () => sendAction('set_output', { index: d.index });
        btn.innerHTML = `<span class="device-name">${esc(d.name)}</span><span class="device-tag ${d.isDedicatedDac ? 'dac' : ''}">${esc(d.kindLabel)}</span>`;
        devList.appendChild(btn);
      });
    }

    // Backends
    const backendsSig = JSON.stringify(s.backends || []);
    if (backendsSig !== lastBackendsSig) {
      lastBackendsSig = backendsSig;
      const bg = document.getElementById('backendGrid');
      bg.innerHTML = '';
      (s.backends || []).forEach(b => {
        const btn = document.createElement('button');
        btn.className = 'seg-btn' + (b.isSelected ? ' active' : '');
        btn.textContent = b.name;
        btn.onclick = () => sendAction('set_backend', { index: b.index });
        bg.appendChild(btn);
      });
    }

    // ASIO drivers
    const asioWrap = document.getElementById('asioDriverWrap');
    const asioSel = document.getElementById('asioSelect');
    const showAsio = (s.asioDrivers || []).length > 0 && s.backendIndex === 1;
    asioWrap.style.display = showAsio ? 'block' : 'none';
    if (showAsio) {
      const asioSig = JSON.stringify(s.asioDrivers || []);
      if (asioSig !== lastAsioSig && document.activeElement !== asioSel) {
        lastAsioSig = asioSig;
        asioSel.innerHTML = s.asioDrivers.map(a => `<option value="${a.index}" ${a.isSelected ? 'selected' : ''}>${esc(a.name)}</option>`).join('');
      }
    }

    // Exclusive
    document.getElementById('exclusiveSwitch').classList.toggle('on', !!s.exclusiveMode);
    document.getElementById('dacHint').textContent = s.hasDedicatedDac
      ? 'DAC dedicado detectado · Exclusivo disponible'
      : 'Salida sin driver DAC dedicado · Exclusivo bloqueado por seguridad';

    // DSD selects (avoid disrupting open mobile dropdowns)
    const dsdModeSel = document.getElementById('dsdModeSelect');
    if (document.activeElement !== dsdModeSel) dsdModeSel.value = String(s.dsdModeIndex ?? 0);
    const dsdGainSel = document.getElementById('dsdGainSelect');
    if (document.activeElement !== dsdGainSel) dsdGainSel.value = String(Math.round(s.dsdGainDb ?? 6));
    const dsdRateSel = document.getElementById('dsdRateSelect');
    if (document.activeElement !== dsdRateSel) dsdRateSel.value = String(s.dsdPcmRate ?? 0);

    // Tone / EQ
    document.getElementById('bypassSwitch').classList.toggle('on', !!s.dspBypass);
    document.getElementById('eqSwitch').classList.toggle('on', !!s.eqEnabled);
    document.getElementById('dspStateLabel').textContent = s.dspBypass ? 'Bypass puro' : (s.eqEnabled ? 'EQ activo' : 'Plano');

    if (!draggingEq) {
      setEqControl('eqPreamp', 'valPreamp', s.preampDb);
      setEqControl('eqSub', 'valSub', s.subBassDb);
      setEqControl('eqPres', 'valPres', s.presenceDb);
      setEqControl('eqAir', 'valAir', s.airDb);
    }
  }

  function setEqControl(id, labelId, val) {
    const el = document.getElementById(id);
    el.value = Number(val || 0);
    setSliderFill(el);
    document.getElementById(labelId).textContent = fmtDb(val);
  }

  // Seek slider events
  const seekEl = document.getElementById('seekSlider');
  seekEl.addEventListener('pointerdown', () => draggingSeek = true);
  seekEl.addEventListener('input', () => {
    draggingSeek = true;
    setSliderFill(seekEl);
    document.getElementById('curTime').textContent = fmtTime(seekEl.value);
  });
  seekEl.addEventListener('change', () => {
    draggingSeek = false;
    sendAction('seek', { seconds: parseFloat(seekEl.value) }, true);
  });

  // Volume slider events
  const volEl = document.getElementById('volSlider');
  volEl.addEventListener('pointerdown', () => draggingVol = true);
  volEl.addEventListener('input', () => {
    draggingVol = true;
    setSliderFill(volEl);
    document.getElementById('volLabel').textContent = volEl.value + '%';
    clearTimeout(volTimer);
    volTimer = setTimeout(() => sendAction('volume', { value: parseFloat(volEl.value) }, true), 80);
  });
  volEl.addEventListener('change', () => {
    draggingVol = false;
    clearTimeout(volTimer);
    sendAction('volume', { value: parseFloat(volEl.value) }, true);
  });

  function nudgeVolume(delta) {
    if (state && state.volumeLocked) {
      showToast('Volumen fijo en modo bit-perfect DSD', true);
      return;
    }
    const next = Math.max(0, Math.min(100, Math.round((state ? state.volume : 100) + delta)));
    volEl.value = next;
    setSliderFill(volEl);
    document.getElementById('volLabel').textContent = next + '%';
    sendAction('volume', { value: next }, true);
  }

  function toggleMute() {
    if (state && state.volumeLocked) return;
    const cur = state ? Math.round(state.volume) : 100;
    const target = cur > 0 ? 0 : Math.max(20, lastNonZeroVolume || 80);
    if (cur > 0) lastNonZeroVolume = cur;
    sendAction('volume', { value: target }, true);
  }

  // EQ sliders
  [['eqPreamp', 'valPreamp'], ['eqSub', 'valSub'], ['eqPres', 'valPres'], ['eqAir', 'valAir']].forEach(([id, lbl]) => {
    const el = document.getElementById(id);
    el.addEventListener('pointerdown', () => draggingEq = true);
    el.addEventListener('input', () => {
      draggingEq = true;
      setSliderFill(el);
      document.getElementById(lbl).textContent = fmtDb(el.value);
      clearTimeout(eqTimer);
      eqTimer = setTimeout(sendEqSliders, 140);
    });
    el.addEventListener('change', () => {
      draggingEq = false;
      clearTimeout(eqTimer);
      sendEqSliders();
    });
  });

  function sendEqSliders() {
    sendAction('set_tone', {
      eqEnabled: true,
      dspBypass: false,
      preamp: parseFloat(document.getElementById('eqPreamp').value),
      subBass: parseFloat(document.getElementById('eqSub').value),
      presence: parseFloat(document.getElementById('eqPres').value),
      air: parseFloat(document.getElementById('eqAir').value)
    }, true);
  }

  function toggleBypass() {
    const next = !(state && state.dspBypass);
    sendAction('set_tone', { dspBypass: next, eqEnabled: next ? false : (state ? state.eqEnabled : false) });
  }

  function toggleEq() {
    const next = !(state && state.eqEnabled);
    sendAction('set_tone', { eqEnabled: next, dspBypass: next ? false : (state ? state.dspBypass : true) });
  }

  function applyTonePreset(preset) {
    sendAction('set_tone', { preset });
  }

  function toggleExclusive() {
    const next = !(state && state.exclusiveMode);
    sendAction('set_exclusive', { enabled: next });
  }

  function onAsioDriverChange() {
    const idx = parseInt(document.getElementById('asioSelect').value, 10);
    sendAction('set_asio_driver', { index: idx });
  }

  function sendDsdConfig() {
    sendAction('set_dsd', {
      mode: parseInt(document.getElementById('dsdModeSelect').value, 10),
      gain: parseFloat(document.getElementById('dsdGainSelect').value),
      rate: parseInt(document.getElementById('dsdRateSelect').value, 10)
    });
  }

  // Library
  function setFilter(f) {
    libFilter = f;
    ['all', 'dsd', 'hires'].forEach(k => document.getElementById('flt-' + k).classList.toggle('active', k === f));
    loadLibrary(false);
  }

  function onSearchInput() {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => loadLibrary(false), 220);
  }

  function clearSearch() {
    document.getElementById('libSearch').value = '';
    loadLibrary(false);
  }

  async function loadLibrary(append) {
    if (!append) { libOffset = 0; libItems = []; }
    const q = encodeURIComponent(document.getElementById('libSearch').value.trim());
    try {
      const res = await apiFetch(`/api/library?q=${q}&filter=${libFilter}&offset=${libOffset}&limit=80`);
      const data = await res.json();
      libItems = append ? libItems.concat(data.items || []) : (data.items || []);
      libOffset = libItems.length;
      document.getElementById('libCounter').textContent = `${data.matchedCount} / ${data.totalLibraryCount} pistas`;
      renderLibrary(data.hasMore);
    } catch { }
  }

  function renderLibrary(hasMore) {
    const wrap = document.getElementById('trackList');
    if (!libItems.length) {
      wrap.innerHTML = '<div class="empty-state">No hay pistas que coincidan. Carga una carpeta en Zenith Audio en tu PC.</div>';
      document.getElementById('loadMoreWrap').style.display = 'none';
      return;
    }

    const curPath = state ? (state.trackPath || '').toLowerCase() : '';
    wrap.innerHTML = '';
    libItems.forEach(t => {
      const btn = document.createElement('button');
      const isCur = t.isCurrent || (curPath && t.path.toLowerCase() === curPath);
      const isDsd = ['.dsf', '.dff', '.iso'].includes((t.extension || '').toLowerCase());
      btn.className = 'track-item' + (isCur ? ' current' : '');
      btn.dataset.path = t.path.toLowerCase();
      btn.onclick = () => sendAction('play_track', { path: t.path });
      btn.innerHTML = `
        <div style="min-width:0">
          <div class="track-item-title">${esc(t.title)}</div>
          <div class="track-item-folder">${esc(t.folder || '')}</div>
        </div>
        <span class="fmt-badge ${isDsd ? 'dsd' : ''}">${esc(t.format || 'AUDIO')}</span>`;
      wrap.appendChild(btn);
    });
    document.getElementById('loadMoreWrap').style.display = hasMore ? 'block' : 'none';
  }

  function highlightCurrentTrack(trackPath) {
    if (!trackPath) return;
    const low = trackPath.toLowerCase();
    document.querySelectorAll('.track-item').forEach(el => {
      el.classList.toggle('current', el.dataset.path === low);
    });
  }

  function esc(str) {
    return String(str ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
  }

  switchTab(activeTab);
  pollState();
  loadLibrary(false);
  setInterval(pollState, 900);
</script>
</body>
</html>
""";
}
