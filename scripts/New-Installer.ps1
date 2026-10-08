<#
.SYNOPSIS
    Prepara l'installer di Quiver (Quiver-win-Setup.exe) e, se vuoi, lo pubblica come release su GitHub.

.DESCRIPTION
    L'installer è fatto con Velopack: installa Quiver per l'utente, senza permessi di amministratore, in
    %LOCALAPPDATA%\Quiver, con il collegamento nel menu Start. Dopo l'installazione Quiver parte con Windows
    e compare nel menu Invia a di Explorer; tutti e due si spengono dalle Impostazioni di Quiver. Si
    disinstalla da Impostazioni > App di Windows.
    Serve il runtime .NET 10 Desktop: se manca, l'installer lo scarica.

    I file finiscono in artifacts\installer: Quiver-win-Setup.exe è quello da lanciare, gli altri servono
    agli aggiornamenti.

.PARAMETER Version
    La versione, per esempio 0.2.0. Se non la indichi, quella di src\Quiver.App\Quiver.App.csproj.

.PARAMETER Publish
    Pubblica la release su GitHub, con le note di novita\<versione>.md. Serve gh, collegato al tuo account.
    Prima controlla che tutto sia nel commit e che il commit sia già su GitHub: il tag della release punta
    proprio a quel commit, così l'installer e il codice della release coincidono.

.PARAMETER Prerelease
    Con -Publish: la release è segnata come "pre-release", da provare.

.EXAMPLE
    .\scripts\New-Installer.ps1
    .\scripts\New-Installer.ps1 -Publish -Prerelease
#>
param(
    [string]$Version,
    [switch]$Publish,
    [switch]$Prerelease
)

$ErrorActionPreference = 'Stop'
$gitHubRepo = 'caste9612/Quiver'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\Quiver.App\Quiver.App.csproj'

# Whether a release with this tag is on GitHub already. gh answers with an error when there is none: here that is not one.
function Test-Release([string]$Tag) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        gh release view $Tag --repo $gitHubRepo *> $null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

if (-not $Version) {
    $Version = ([xml](Get-Content -LiteralPath $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$notes = Join-Path $repo "novita\$Version.md"
if (-not (Test-Path -LiteralPath $notes)) {
    throw "Mancano le note della versione: $notes"
}

# A published installer must match a commit anyone can find on GitHub: nothing left out of it, and already pushed.
# Checked before building, so that a missing push does not cost a whole build.
if ($Publish) {
    $changes = git -C $repo status --porcelain --untracked-files=no
    if ($changes) {
        throw "Ci sono modifiche non ancora nel commit: fai prima il commit, così la release corrisponde al codice.`n$($changes -join "`n")"
    }
    git -C $repo fetch --quiet origin
    if ($LASTEXITCODE -ne 0) { throw 'Non riesco a leggere il repository su GitHub.' }
    $commit = git -C $repo rev-parse HEAD
    if (-not (git -C $repo branch --remotes --contains $commit)) {
        throw "Il commit $($commit.Substring(0, 7)) non è ancora su GitHub: fai prima git push."
    }
    if (Test-Release "v$Version") {
        throw "Su GitHub c'è già la release v${Version}: alza la versione in $project e scrivi le note."
    }
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
    Write-Host "Pubblico la release v$Version su GitHub, dal commit $($commit.Substring(0, 7))..."
    $assets = (Get-ChildItem -LiteralPath $installerFolder -File).FullName
    $options = if ($Prerelease) { @('--prerelease') } else { @() }
    # The tag goes on the commit just built, not on whatever the main branch of GitHub holds now.
    gh release create "v$Version" @assets --repo $gitHubRepo --target $commit --title "Quiver $Version" --notes-file $notes @options
    if ($LASTEXITCODE -ne 0) { throw 'La pubblicazione su GitHub non è riuscita.' }
}
