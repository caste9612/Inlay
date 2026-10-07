<#
.SYNOPSIS
    Installa Quiver per l'uso di tutti i giorni, o lo aggiorna all'ultima versione del codice.

.DESCRIPTION
    Pubblica Quiver in una cartella fissa, separata da quella in cui si compila: la copia di tutti i giorni
    resta aperta mentre lo sviluppo va avanti. Se Quiver è aperto, lo script prima lo chiude e alla fine
    lo riapre.

    Al primo avvio Quiver chiede quale cartella usare come archivio. "Avvia con Windows" si attiva dal
    menu dell'icona nella tray: vale per la copia da cui lo attivi.

.PARAMETER Destination
    Dove installarlo. Se non lo indichi: %LOCALAPPDATA%\Programs\Quiver.

.EXAMPLE
    .\scripts\Install-Quiver.ps1
#>
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\Quiver')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\Quiver.App\Quiver.App.csproj'

# Quiver runs as a single instance: the open copy, wherever it is, is asked to close so its files can be replaced.
$running = @(Get-Process Quiver -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host 'Chiudo Quiver...'
    & $running[0].Path --exit
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    if (Get-Process Quiver -ErrorAction SilentlyContinue) {
        throw "Quiver non si è chiuso: chiudilo dall'icona nella tray (Esci) e riprova."
    }
}

Write-Host "Pubblico Quiver in $Destination..."
# ReadyToRun: compiled ahead of time, so it starts faster with Windows.
dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $Destination --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw 'La pubblicazione non è riuscita: guarda gli errori qui sopra.'
}

Start-Process (Join-Path $Destination 'Quiver.exe')
Write-Host "Fatto. Quiver è nella tray: premi Alt+E per aprirlo."
Write-Host "Per farlo partire con Windows: tasto destro sull'icona nella tray, Avvia con Windows."
