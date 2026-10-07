<#
.SYNOPSIS
    Prepara l'installer di Quiver (Quiver-win-Setup.exe) e, se vuoi, lo pubblica come release su GitHub.

.DESCRIPTION
    L'installer è fatto con Velopack: installa Quiver per l'utente, senza permessi di amministratore, in
    %LOCALAPPDATA%\Quiver, con il collegamento nel menu Start. Dopo l'installazione Quiver parte con Windows
    (si disattiva dal menu della tray) e si disinstalla da Impostazioni > App.
    Serve il runtime .NET 10 Desktop: se manca, l'installer lo scarica.

    I file finiscono in artifacts\installer: Quiver-win-Setup.exe è quello da lanciare, gli altri servono
    agli aggiornamenti.

.PARAMETER Version
    La versione, per esempio 0.2.0. Se non la indichi, quella di src\Quiver.App\Quiver.App.csproj.

.PARAMETER Publish
    Pubblica la release su GitHub, con le note di novita\<versione>.md. Serve gh, collegato al tuo account.

.PARAMETER Prerelease
    Con -Publish: la release è segnata come "pre-release", da provare.

.EXAMPLE
    .\scripts\New-Installer.ps1
    .\scripts\New-Installer.ps1 -Publish
#>
param(
    [string]$Version,
    [switch]$Publish,
    [switch]$Prerelease
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\Quiver.App\Quiver.App.csproj'
if (-not $Version) {
    $Version = ([xml](Get-Content -LiteralPath $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$notes = Join-Path $repo "novita\$Version.md"
if (-not (Test-Path -LiteralPath $notes)) {
    throw "Mancano le note della versione: $notes"
}
$publishFolder = Join-Path $repo 'artifacts\publish'
$installerFolder = Join-Path $repo 'artifacts\installer'
foreach ($folder in $publishFolder, $installerFolder) {
    if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
}

Write-Host "Compilo Quiver $Version..."
# ReadyToRun: compiled ahead of time, so it starts faster with Windows.
dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -p:Version=$Version `
    -o $publishFolder --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La compilazione non è riuscita: guarda gli errori qui sopra.' }

Write-Host "Preparo l'installer..."
dotnet tool restore | Out-Null
# Only the Start menu: a shortcut on the Desktop would be odd for a program that takes its place.
dotnet vpk pack --runtime win-x64 --packId Quiver --packVersion $Version --packTitle Quiver --packAuthors 'Antonio Castellucci' `
    --packDir $publishFolder --mainExe Quiver.exe --icon (Join-Path $repo 'src\Quiver.App\Assets\quiver.ico') `
    --framework net10-x64-desktop --shortcuts StartMenuRoot --releaseNotes $notes --outputDir $installerFolder
if ($LASTEXITCODE -ne 0) { throw "L'installer non è stato creato: guarda gli errori qui sopra." }

$setup = Get-ChildItem -LiteralPath $installerFolder -Filter '*Setup.exe' | Select-Object -First 1
Write-Host "Installer pronto: $($setup.FullName)"

if ($Publish) {
    Write-Host "Pubblico la release v$Version su GitHub..."
    $assets = (Get-ChildItem -LiteralPath $installerFolder -File).FullName
    $options = if ($Prerelease) { @('--prerelease') } else { @() }
    gh release create "v$Version" @assets --repo caste9612/Quiver --title "Quiver $Version" --notes-file $notes @options
    if ($LASTEXITCODE -ne 0) { throw 'La pubblicazione su GitHub non è riuscita.' }
}
