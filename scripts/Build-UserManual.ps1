param(
    [string]$PandocPath = "",
    [string]$BrowserPath = "",
    [string]$Version = "1.0.6"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$manual = Join-Path $root "docs\MANUAL_DE_USUARIO.md"
$template = Join-Path $root "docs\manual-template.html"
$cssFromHtml = "..\..\docs\manual-pdf.css"
$logoFromHtml = "..\..\src\ZenithAudio\Assets\Logo.png"
$artifactDir = Join-Path $root "artifacts\manual"
$html = Join-Path $artifactDir "ZenithAudio_Manual_de_Usuario.html"
$pdf = Join-Path $root "docs\ZenithAudio_Manual_de_Usuario.pdf"

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

if ([string]::IsNullOrWhiteSpace($PandocPath)) {
    $cmd = Get-Command "pandoc.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        $PandocPath = $cmd.Source
    } else {
        $PandocPath = "C:\Program Files\Pandoc\pandoc.exe"
    }
}

if (-not (Test-Path $PandocPath)) {
    throw "No se encontro pandoc.exe. Usa -PandocPath con la ruta correcta."
}

if ([string]::IsNullOrWhiteSpace($BrowserPath)) {
    $browserCandidates = @(
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    )

    $BrowserPath = $browserCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not (Test-Path $BrowserPath)) {
    throw "No se encontro Chrome o Edge para imprimir el HTML a PDF."
}

& $PandocPath $manual `
    --from "gfm+smart" `
    --to "html5" `
    --standalone `
    --toc `
    --toc-depth 2 `
    --template $template `
    --css $cssFromHtml `
    --metadata "pagetitle=Manual Zenith Audio Player $Version" `
    --metadata "subtitle=Reproductor Hi-Res para Windows, biblioteca local, DSD, CUE, letras y ZenithAI" `
    --metadata "version=$Version" `
    --metadata "logo=$logoFromHtml" `
    --output $html

$htmlUri = (New-Object System.Uri($html)).AbsoluteUri
& $BrowserPath `
    --headless=new `
    --disable-gpu `
    --no-pdf-header-footer `
    --run-all-compositor-stages-before-draw `
    --virtual-time-budget=1500 `
    "--print-to-pdf=$pdf" `
    $htmlUri | Out-Null

if (-not (Test-Path $pdf)) {
    throw "No se genero el PDF esperado: $pdf"
}

Get-Item $pdf | Select-Object FullName, Length, LastWriteTime
