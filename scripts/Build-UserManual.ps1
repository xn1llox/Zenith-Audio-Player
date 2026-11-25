param(
    [string]$PandocPath = "",
    [string]$BrowserPath = "",
    [string]$Version = "1.0.8"
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
$subtitle = "Reproductor Hi-Res para Windows: ASIO DSD Nativo, WASAPI, biblioteca masiva, ZenithAI, Control Remoto Web y Auto-Update"

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

function Escape-Html([string]$text) {
    return [System.Net.WebUtility]::HtmlEncode($text)
}

function Convert-InlineMarkdown([string]$text) {
    $codeTokens = New-Object System.Collections.Generic.List[string]
    $work = [regex]::Replace($text, '`([^`]+)`', {
        param($m)
        $idx = $codeTokens.Count
        $codeTokens.Add("<code>$(Escape-Html $m.Groups[1].Value)</code>")
        return "@@CODE_${idx}@@"
    })

    $work = Escape-Html $work

    # Markdown links [label](url)
    $work = [regex]::Replace($work, '\[([^\]]+)\]\((https?://[^\s\)]+)\)', '<a href="$2">$1</a>')

    # Bare https:// URLs not already inside href="..." or >...</a>
    $work = [regex]::Replace($work, '(?<!href=")(?<!">)(https?://[^\s<]+[^\s<\.,;:\)])', '<a href="$1">$1</a>')

    # Bold and italic
    $work = [regex]::Replace($work, '\*\*([^\*]+)\*\*', '<strong>$1</strong>')
    $work = [regex]::Replace($work, '(?<!\*)\*([^\*]+)\*(?!\*)', '<em>$1</em>')

    for ($i = 0; $i -lt $codeTokens.Count; $i++) {
        $work = $work.Replace("@@CODE_${i}@@", $codeTokens[$i])
    }

    return $work
}

function New-Slug([string]$title) {
    $norm = $title.Normalize([Text.NormalizationForm]::FormD)
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $norm.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$sb.Append([char]::ToLowerInvariant($ch))
        }
    }
    $slug = [regex]::Replace($sb.ToString(), '[^a-z0-9]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($slug)) { return "sec" }
    return $slug
}

function Render-MarkdownWithBuiltInConverter([string]$mdPath, [string]$templatePath, [string]$outHtmlPath) {
    $lines = [System.IO.File]::ReadAllLines($mdPath, [System.Text.Encoding]::UTF8)
    $body = New-Object System.Text.StringBuilder
    $tocEntries = New-Object System.Collections.Generic.List[object]
    $usedIds = @{}

    $i = 0
    while ($i -lt $lines.Length) {
        $line = $lines[$i]
        $trimmed = $line.Trim()

        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            $i++
            continue
        }

        # Fenced code block
        if ($trimmed.StartsWith('```')) {
            $codeLines = New-Object System.Collections.Generic.List[string]
            $i++
            while ($i -lt $lines.Length -and -not $lines[$i].Trim().StartsWith('```')) {
                $codeLines.Add((Escape-Html $lines[$i]))
                $i++
            }
            if ($i -lt $lines.Length) { $i++ }
            [void]$body.AppendLine("<pre><code>$($codeLines -join "`n")</code></pre>")
            continue
        }

        # Headings (#, ##, ###)
        $hMatch = [regex]::Match($trimmed, '^(#{1,3})\s+(.+)$')
        if ($hMatch.Success) {
            $level = $hMatch.Groups[1].Value.Length
            $rawTitle = $hMatch.Groups[2].Value.Trim()
            $baseId = New-Slug $rawTitle
            $id = $baseId
            $n = 2
            while ($usedIds.ContainsKey($id)) {
                $id = "$baseId-$n"
                $n++
            }
            $usedIds[$id] = $true

            $htmlTitle = Convert-InlineMarkdown $rawTitle
            [void]$body.AppendLine("<h$level id=`"$id`">$htmlTitle</h$level>")
            if ($level -eq 2 -or $level -eq 3) {
                $tocEntries.Add([pscustomobject]@{ Level = $level; Id = $id; HtmlTitle = $htmlTitle })
            }
            $i++
            continue
        }

        # Blockquote
        if ($trimmed.StartsWith('>')) {
            $qLines = New-Object System.Collections.Generic.List[string]
            while ($i -lt $lines.Length -and $lines[$i].Trim().StartsWith('>')) {
                $qLines.Add($lines[$i].Trim().Substring(1).Trim())
                $i++
            }
            $qHtml = ($qLines | ForEach-Object { Convert-InlineMarkdown $_ }) -join "<br>"
            [void]$body.AppendLine("<blockquote><p>$qHtml</p></blockquote>")
            continue
        }

        # Pipe table
        if ($trimmed.StartsWith('|') -and ($i + 1) -lt $lines.Length -and $lines[$i + 1].Trim() -match '^\|[\s\-\:\|]+\|$') {
            $headerCells = $trimmed.Trim('|').Split('|') | ForEach-Object { $_.Trim() }
            [void]$body.AppendLine("<table><thead><tr>")
            foreach ($cell in $headerCells) {
                [void]$body.AppendLine("<th>$(Convert-InlineMarkdown $cell)</th>")
            }
            [void]$body.AppendLine("</tr></thead><tbody>")
            $i += 2
            while ($i -lt $lines.Length -and $lines[$i].Trim().StartsWith('|')) {
                $rowCells = $lines[$i].Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
                [void]$body.AppendLine("<tr>")
                foreach ($cell in $rowCells) {
                    [void]$body.AppendLine("<td>$(Convert-InlineMarkdown $cell)</td>")
                }
                [void]$body.AppendLine("</tr>")
                $i++
            }
            [void]$body.AppendLine("</tbody></table>")
            continue
        }

        # Unordered list
        if ($trimmed -match '^[-*]\s+(.+)$') {
            [void]$body.AppendLine("<ul>")
            while ($i -lt $lines.Length -and $lines[$i].Trim() -match '^[-*]\s+(.+)$') {
                $item = $Matches[1]
                [void]$body.AppendLine("<li>$(Convert-InlineMarkdown $item)</li>")
                $i++
            }
            [void]$body.AppendLine("</ul>")
            continue
        }

        # Ordered list
        if ($trimmed -match '^\d+\.\s+(.+)$') {
            [void]$body.AppendLine("<ol>")
            while ($i -lt $lines.Length -and $lines[$i].Trim() -match '^\d+\.\s+(.+)$') {
                $item = $Matches[1]
                [void]$body.AppendLine("<li>$(Convert-InlineMarkdown $item)</li>")
                $i++
            }
            [void]$body.AppendLine("</ol>")
            continue
        }

        # Paragraph (handles trailing two spaces as <br>)
        $pParts = New-Object System.Collections.Generic.List[string]
        while ($i -lt $lines.Length) {
            $cur = $lines[$i]
            $curTrim = $cur.Trim()
            if ([string]::IsNullOrWhiteSpace($curTrim) -or
                $curTrim.StartsWith('#') -or
                $curTrim.StartsWith('```') -or
                $curTrim.StartsWith('>') -or
                $curTrim.StartsWith('|') -or
                $curTrim -match '^[-*]\s+' -or
                $curTrim -match '^\d+\.\s+') {
                break
            }
            $hasHardBreak = $cur.EndsWith("  ")
            $rendered = Convert-InlineMarkdown $curTrim
            $pParts.Add($rendered + $(if ($hasHardBreak) { "<br>" } else { " " }))
            $i++
        }
        $pContent = ($pParts -join "").Trim()
        [void]$body.AppendLine("<p>$pContent</p>")
    }

    # Build nested TOC (Level 2 items only so TOC fits cleanly on a single page)
    $tocSb = New-Object System.Text.StringBuilder
    [void]$tocSb.AppendLine("<ul>")
    foreach ($entry in $tocEntries) {
        if ($entry.Level -eq 2) {
            [void]$tocSb.AppendLine("  <li><a href=`"#$($entry.Id)`">$($entry.HtmlTitle)</a></li>")
        }
    }
    [void]$tocSb.AppendLine("</ul>")

    $tpl = [System.IO.File]::ReadAllText($templatePath, [System.Text.Encoding]::UTF8)
    $tpl = $tpl.Replace('$if(lang)$$lang$$else$es$endif$', 'es')
    $tpl = $tpl.Replace('$pagetitle$', (Escape-Html "Manual Zenith Audio Player $Version"))
    $tpl = [regex]::Replace($tpl, '\$for\(css\)\$.*?\$endfor\$', "<link rel=`"stylesheet`" href=`"$cssFromHtml`">", [Text.RegularExpressions.RegexOptions]::Singleline)
    $tpl = $tpl.Replace('$logo$', $logoFromHtml)
    $tpl = $tpl.Replace('$version$', (Escape-Html $Version))
    $tpl = $tpl.Replace('$subtitle$', (Escape-Html $subtitle))
    $tpl = $tpl.Replace('$if(toc)$', '')
    $tpl = $tpl.Replace('$endif$', '')
    $tpl = $tpl.Replace('$idprefix$', '')
    $tpl = $tpl.Replace('$table-of-contents$', $tocSb.ToString())
    $tpl = $tpl.Replace('$body$', $body.ToString())

    [System.IO.File]::WriteAllText($outHtmlPath, $tpl, (New-Object System.Text.UTF8Encoding($false)))
}

if ([string]::IsNullOrWhiteSpace($PandocPath)) {
    $cmd = Get-Command "pandoc.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        $PandocPath = $cmd.Source
    } elseif (Test-Path "C:\Program Files\Pandoc\pandoc.exe") {
        $PandocPath = "C:\Program Files\Pandoc\pandoc.exe"
    }
}

if ([string]::IsNullOrWhiteSpace($BrowserPath)) {
    $browserCandidates = @(
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    )
    $BrowserPath = $browserCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not (Test-Path $BrowserPath)) {
    throw "No se encontro Chrome o Edge para imprimir el HTML a PDF."
}

if (-not [string]::IsNullOrWhiteSpace($PandocPath) -and (Test-Path $PandocPath)) {
    & $PandocPath $manual `
        --from "gfm+smart" `
        --to "html5" `
        --standalone `
        --toc `
        --toc-depth 2 `
        --template $template `
        --css $cssFromHtml `
        --metadata "pagetitle=Manual Zenith Audio Player $Version" `
        --metadata "subtitle=$subtitle" `
        --metadata "version=$Version" `
        --metadata "logo=$logoFromHtml" `
        --output $html
} else {
    Write-Host "Pandoc no encontrado; usando generador HTML integrado."
    Render-MarkdownWithBuiltInConverter -mdPath $manual -templatePath $template -outHtmlPath $html
}

$htmlUri = (New-Object System.Uri($html)).AbsoluteUri
& $BrowserPath `
    --headless=new `
    --disable-gpu `
    --no-pdf-header-footer `
    --run-all-compositor-stages-before-draw `
    --virtual-time-budget=2000 `
    "--print-to-pdf=$pdf" `
    $htmlUri | Out-Null

if (-not (Test-Path $pdf)) {
    throw "No se genero el PDF esperado: $pdf"
}

Get-Item $pdf | Select-Object FullName, Length, LastWriteTime
