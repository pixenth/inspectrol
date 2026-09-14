param(
    # makensis.exe from NSIS 3 (https://nsis.sourceforge.io). Not needed with -SkipInstaller.
    [string] $Makensis = 'makensis',
    [switch] $SkipTests,
    [switch] $SkipInstaller,
    # Folder on the site where the installer will be uploaded; artifacts/update.json points there.
    [string] $DownloadUrl = 'https://inspectrol.ru/download',
    [string] $Notes = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Inspectrol.App\Inspectrol.App.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts "Inspectrol-$version-win-x64"

function Invoke-Native([string] $name, [scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE" }
}

if (-not $SkipTests) {
    Invoke-Native 'dotnet test' { dotnet test $root -c Release -nologo }
}

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

# Self-contained, so users do not need the .NET Desktop Runtime installed.
Invoke-Native 'dotnet publish' {
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish -nologo `
        -p:DebugType=None -p:DebugSymbols=false
}

if (-not $SkipInstaller) {
    $setup = Join-Path $artifacts "Inspectrol-$version-setup.exe"
    Invoke-Native 'makensis' {
        & $Makensis /V2 /INPUTCHARSET UTF8 `
            "/DVERSION=$version" `
            "/DPUBLISH_DIR=$publish" `
            "/DLICENSE_FILE=$(Join-Path $root 'LICENSE')" `
            "/DICON=$(Join-Path $root 'src\Inspectrol.App\Assets\inspectrol.ico')" `
            "/DOUTPUT=$setup" `
            (Join-Path $root 'installer\Inspectrol.nsi')
    }
    Get-Item $setup | Select-Object Name, @{ Name = 'MB'; Expression = { [math]::Round($_.Length / 1MB, 1) } }

    # Served at the address the app checks for updates, see docs/update-server.md.
    $manifest = [ordered]@{
        version = $version
        url = $DownloadUrl.TrimEnd('/') + '/' + (Split-Path $setup -Leaf)
        size = (Get-Item $setup).Length
        sha256 = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        notes = $Notes
    }
    $json = Join-Path $artifacts 'update.json'
    [IO.File]::WriteAllText($json, ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    "update.json -> $json"
}
