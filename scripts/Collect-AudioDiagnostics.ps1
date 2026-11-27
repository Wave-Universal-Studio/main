<#
.SYNOPSIS
    Coleta informações básicas e somente leitura de ambiente e dispositivos de áudio
    para facilitar o relato de bugs do Wave Studio no Windows.

.DESCRIPTION
    Este script NÃO grava áudio, NÃO altera volumes, NÃO troca o dispositivo padrão
    e NÃO instala ou modifica drivers. Todos os caminhos coletados têm o nome do
    usuário anonimizado antes de serem gravados.

.PARAMETER OutputPath
    Pasta onde o pacote de diagnóstico (.zip ou diretório) será salvo.
    Padrão: pasta temporária do usuário.

.PARAMETER NoZip
    Quando informado, mantém os arquivos em uma pasta em vez de compactar em .zip.

.PARAMETER IncludeSettings
    Quando informado, inclui arquivos .json locais de configuração do Wave Studio
    (desligado por padrão para maior privacidade).
#>
[CmdletBinding()]
param(
    [string]$OutputPath = $env:TEMP,
    [switch]$NoZip,
    [switch]$IncludeSettings
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Protect-UserPath {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    $resultado = $Text
    if ($env:USERPROFILE) {
        $resultado = $resultado -replace [regex]::Escape($env:USERPROFILE), '%USERPROFILE%'
    }
    if ($env:USERNAME) {
        $resultado = $resultado -replace [regex]::Escape($env:USERNAME), '<USUARIO>'
    }
    return $resultado
}

$carimbo = Get-Date -Format 'yyyyMMdd-HHmmss'
$pastaTrabalho = Join-Path ([System.IO.Path]::GetTempPath()) "wavestudio-diag-$carimbo"
New-Item -ItemType Directory -Path $pastaTrabalho -Force | Out-Null

try {
    # 1. Informações básicas do sistema operacional e runtime .NET (somente leitura).
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $dotnetVersoes = @()
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCmd) {
        $dotnetVersoes = (& dotnet --list-runtimes 2>&1 | ForEach-Object { Protect-UserPath "$_" })
    } else {
        $dotnetVersoes = @('dotnet CLI não encontrado no PATH')
    }

    $infoSistema = @(
        "Data UTC: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
        "Sistema: $($os.Caption)"
        "Versão: $($os.Version) (Build $($os.BuildNumber))"
        "Arquitetura SO: $($os.OSArchitecture)"
        "Arquitetura Processo: $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)"
        "Cultura atual: $([System.Globalization.CultureInfo]::CurrentCulture.Name)"
        "Cultura UI: $([System.Globalization.CultureInfo]::CurrentUICulture.Name)"
        ""
        "Runtimes .NET:"
    ) + $dotnetVersoes

    [System.IO.File]::WriteAllLines((Join-Path $pastaTrabalho '01-sistema.txt'), $infoSistema, [System.Text.UTF8Encoding]::new($false))

    # 2. Controladores e endpoints de áudio expostos pelo Windows (somente leitura via CIM).
    $controladores = Get-CimInstance -ClassName Win32_SoundDevice |
        Select-Object Name, Manufacturer, Status, StatusInfo |
        Format-Table -AutoSize | Out-String -Width 200

    $endpoints = Get-CimInstance -ClassName Win32_PnPEntity -Filter "PNPClass = 'AudioEndpoint' OR PNPClass = 'MEDIA'" |
        Sort-Object PNPClass, Name |
        Select-Object PNPClass, Status, Name, Manufacturer |
        Format-Table -AutoSize | Out-String -Width 200

    $textoAudio = @(
        '=== Controladores de Som (Win32_SoundDevice) ==='
        $controladores.TrimEnd()
        ''
        '=== Endpoints e Dispositivos de Mídia (Win32_PnPEntity) ==='
        $endpoints.TrimEnd()
    )
    [System.IO.File]::WriteAllLines((Join-Path $pastaTrabalho '02-dispositivos-audio.txt'), $textoAudio, [System.Text.UTF8Encoding]::new($false))

    # 3. Presença de aplicativos comuns de áudio/streaming (apenas sim/não, sem listar todos os processos).
    $alvoProcessos = @('WaveStudio', 'obs64', 'Discord', 'voicemeeter', 'voicemeeterpro', 'voicemeeter8', 'EqualizerAPO', 'audiodg')
    $ativos = @(Get-Process -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ProcessName -Unique)
    $linhasProc = foreach ($nome in $alvoProcessos) {
        $presente = if ($ativos -contains $nome) { 'Ativo' } else { 'Não detectado' }
        "{0,-18}: {1}" -f $nome, $presente
    }
    [System.IO.File]::WriteAllLines((Join-Path $pastaTrabalho '03-processos-audio.txt'), $linhasProc, [System.Text.UTF8Encoding]::new($false))

    # 4. Metadados de configuração local do Wave Studio (sem exportar conteúdo por padrão).
    $possiveisPastas = @(
        @(
            (Join-Path $env:APPDATA 'WaveStudio'),
            (Join-Path $env:LOCALAPPDATA 'WaveStudio')
        ) | Where-Object { $_ -and (Test-Path $_) }
    )

    $linhasConfig = @('=== Pastas locais do Wave Studio ===')
    if ($possiveisPastas.Count -eq 0) {
        $linhasConfig += 'Nenhuma pasta local de configuração encontrada.'
    } else {
        foreach ($dir in $possiveisPastas) {
            $linhasConfig += "Pasta: $(Protect-UserPath $dir)"
            $arquivos = Get-ChildItem -Path $dir -Recurse -File -ErrorAction SilentlyContinue
            foreach ($arq in $arquivos) {
                $linhasConfig += "  - $(Protect-UserPath $arq.FullName) ($($arq.Length) bytes)"
                if ($IncludeSettings -and $arq.Extension -eq '.json' -and $arq.Length -le 262144) {
                    $destino = Join-Path $pastaTrabalho ("config-" + $arq.Name)
                    $conteudo = Protect-UserPath (Get-Content -Path $arq.FullName -Raw -ErrorAction SilentlyContinue)
                    [System.IO.File]::WriteAllText($destino, $conteudo, [System.Text.UTF8Encoding]::new($false))
                }
            }
        }
    }
    [System.IO.File]::WriteAllLines((Join-Path $pastaTrabalho '04-configuracoes.txt'), $linhasConfig, [System.Text.UTF8Encoding]::new($false))

    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    if ($NoZip) {
        $destinoFinal = Join-Path $OutputPath "wavestudio-diagnostico-$carimbo"
        Copy-Item -Path $pastaTrabalho -Destination $destinoFinal -Recurse -Force
        Write-Host "Diagnóstico salvo em: $destinoFinal"
    } else {
        $zipFinal = Join-Path $OutputPath "wavestudio-diagnostico-$carimbo.zip"
        Compress-Archive -Path (Join-Path $pastaTrabalho '*') -DestinationPath $zipFinal -Force
        Write-Host "Pacote de diagnóstico gerado em: $zipFinal"
    }
}
finally {
    if (Test-Path $pastaTrabalho) {
        Remove-Item -Path $pastaTrabalho -Recurse -Force -ErrorAction SilentlyContinue
    }
}