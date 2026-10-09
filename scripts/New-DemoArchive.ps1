<#
.SYNOPSIS
    Crea un archivio di prova per Quiver: argomenti annidati, riquadri, una cartella tra le righe di «Generale»,
    programmi, siti e un file sciolto nella radice.

.EXAMPLE
    .\scripts\New-DemoArchive.ps1 -Path D:\ArchivioDemo
#>
param(
    [Parameter(Mandatory)]
    [string] $Path
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Path) {
    throw "La cartella '$Path' esiste già: indicane una nuova, lo script non tocca cartelle esistenti."
}

function New-DemoFile([string] $RelativePath, [string] $Content = '') {
    $file = Join-Path $Path $RelativePath
    New-Item -ItemType Directory -Force -Path (Split-Path $file) | Out-Null
    Set-Content -LiteralPath $file -Value $Content -Encoding utf8
}

function New-DemoFolder([string] $RelativePath) {
    New-Item -ItemType Directory -Force -Path (Join-Path $Path $RelativePath) | Out-Null
}

function New-DemoShortcut([string] $RelativePath, [string] $Target) {
    $file = Join-Path $Path $RelativePath
    New-Item -ItemType Directory -Force -Path (Split-Path $file) | Out-Null
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($file)
    $shortcut.TargetPath = $Target
    $shortcut.Save()
}

function New-DemoWebLink([string] $RelativePath, [string] $Url) {
    New-DemoFile $RelativePath "[InternetShortcut]`r`nURL=$Url"
}

New-DemoFile 'Lavoro\Promemoria.txt'
New-DemoFile 'Lavoro\Clienti\Rossi\Note.md' '# Note sul cliente Rossi'
New-DemoFile 'Lavoro\Clienti\Rossi\Documenti\Offerta_v3.txt' 'Offerta per il cliente Rossi.'
New-DemoFile 'Lavoro\Clienti\Rossi\Documenti\Verbale riunione 2.txt'
New-DemoFile 'Lavoro\Clienti\Rossi\Documenti\Verbale riunione 10.txt'
New-DemoFolder 'Lavoro\Clienti\Rossi\Documenti\Commessa 2026'
New-DemoShortcut 'Lavoro\Clienti\Rossi\Programmi\Calcolatrice.lnk' "$env:WINDIR\System32\calc.exe"
New-DemoShortcut 'Lavoro\Clienti\Rossi\Programmi\Blocco note.lnk' "$env:WINDIR\System32\notepad.exe"
New-DemoFile 'Lavoro\Clienti\Bianchi\Documenti\Contratto.txt'
New-DemoFile 'Lavoro\Clienti\Bianchi\Disegni\Pianta piano terra.txt'
New-DemoFile 'Lavoro\Interni\Procedure\Ferie e permessi.txt'
New-DemoFile 'Lavoro\Interni\Modelli\Lettera standard.txt'
New-DemoFile 'Personale\Casa\Bollette 2026.csv' 'mese;importo'
New-DemoFile 'Personale\Viaggi\Perché andare a Lisbona.md' '# Lisbona'
New-DemoFile 'Personale\Foto delle vacanze\Lisbona 1.txt'
New-DemoFile 'Personale\Foto delle vacanze\Lisbona 2.txt'
New-DemoShortcut 'Strumenti\Programmi\Blocco note.lnk' "$env:WINDIR\System32\notepad.exe"
New-DemoShortcut 'Strumenti\Programmi\Prompt dei comandi.lnk' "$env:WINDIR\System32\cmd.exe"
New-DemoShortcut 'Strumenti\Cartelle\Download.lnk' (Join-Path $env:USERPROFILE 'Downloads')
New-DemoWebLink 'Strumenti\Siti\GlazeWM.url' 'https://github.com/glzr-io/glazewm'
New-DemoFile 'documento scaricato.txt'

# Clienti, Rossi, Bianchi e Interni sono sotto-argomenti: compaiono nel menu laterale invece che come riquadri.
# «Foto delle vacanze» è una riga di «Generale» in Personale, come una cartella trascinata sull'argomento.
$metadata = New-Item -ItemType Directory -Force -Path (Join-Path $Path '.quiver')
$metadata.Attributes = $metadata.Attributes -bor [IO.FileAttributes]::Hidden
@'
{
  "version": 1,
  "folders": {
    "Lavoro/Clienti": { "kind": "topic" },
    "Lavoro/Clienti/Rossi": { "kind": "topic" },
    "Lavoro/Clienti/Bianchi": { "kind": "topic" },
    "Lavoro/Interni": { "kind": "topic" },
    "Personale/Foto delle vacanze": { "kind": "item" }
  }
}
'@ | Set-Content -LiteralPath (Join-Path $metadata.FullName 'archive.json') -Encoding utf8

Write-Host "Archivio di prova creato in $Path"
