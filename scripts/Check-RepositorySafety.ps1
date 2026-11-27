<#
.SYNOPSIS
    Varre os arquivos versionados do repositório em busca de segredos ou arquivos proibidos.

.DESCRIPTION
    Executa sem dependências externas (somente git + PowerShell) e nunca imprime o valor
    do segredo encontrado — apenas o caminho do arquivo e o motivo.
    Retorna código de saída 1 caso encontre qualquer ocorrência.
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Padrões de conteúdo sensível. Alguns delimitadores usam quantificador ({5}) para
# evitar que o próprio script dispare falso positivo ao varrer a si mesmo.
$padroesConteudo = @(
    @{ Motivo = 'chave privada PEM';      Regex = '-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-{5}' }
    @{ Motivo = 'chave privada PGP';      Regex = '-----BEGIN PGP PRIVATE KEY BLOCK-{5}' }
    @{ Motivo = 'JWK com chave privada';  Regex = '"d"\s*:\s*"[A-Za-z0-9_-]{32,}"' }
    @{ Motivo = 'token do GitHub';        Regex = '\bgh[pousr]_[A-Za-z0-9]{36,}\b' }
    @{ Motivo = 'token PAT do GitHub';    Regex = '\bgithub_pat_[A-Za-z0-9_]{22,}\b' }
    @{ Motivo = 'chave de API (sk-)';     Regex = '\bsk-(?:ant-)?[A-Za-z0-9_-]{24,}\b' }
    @{ Motivo = 'chave de acesso AWS';    Regex = '\bAKIA[0-9A-Z]{16}\b' }
    @{ Motivo = 'chave do Resend';        Regex = '\bre_(?=[A-Za-z0-9_]*\d)(?=[A-Za-z0-9_]*[A-Z])[A-Za-z0-9_]{20,}\b' }
)

# Extensões binárias conhecidas que não precisam de varredura textual.
$extensoesBinarias = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@('.png', '.ico', '.jpg', '.jpeg', '.webp', '.gif', '.bmp', '.wav', '.mp3', '.ogg', '.flac', '.zip', '.7z', '.exe', '.dll', '.pdb', '.nupkg', '.ttf', '.otf', '.woff', '.woff2'),
    [System.StringComparer]::OrdinalIgnoreCase
)

function Test-ArquivoProibido {
    param([string]$RelativePath)
    $normalizado = $RelativePath -replace '\\', '/'
    $nome = [System.IO.Path]::GetFileName($normalizado)

    if ($nome -match '^\.env(\..+)?$' -and $nome -notmatch '\.(example|exemplo|sample)$') {
        return 'arquivo .env versionado'
    }
    if ($nome -match '^id_(rsa|dsa|ecdsa|ed25519)(_sk)?$') {
        return 'chave privada SSH'
    }
    if ($nome -match '\.(p12|pfx|key)$') {
        return 'certificado ou chave privada'
    }
    if ($nome -match '\.(db|sqlite|sqlite3)(-journal|-wal)?$') {
        return 'banco de dados local'
    }
    if ($nome -ieq 'auth.json') {
        return 'arquivo de credenciais (auth.json)'
    }
    return $null
}

$saidaGit = @(& git -C $RepositoryRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao listar arquivos versionados com git ls-files (código $LASTEXITCODE)."
}

$achados = [System.Collections.Generic.List[object]]::new()
$limiteBytes = 2 * 1024 * 1024

foreach ($relativo in $saidaGit) {
    if ([string]::IsNullOrWhiteSpace($relativo)) { continue }
    $normalizado = $relativo -replace '\\', '/'

    $motivoNome = Test-ArquivoProibido -RelativePath $normalizado
    if ($motivoNome) {
        $achados.Add([pscustomobject]@{ Arquivo = $normalizado; Motivo = $motivoNome })
        continue
    }

    $ext = [System.IO.Path]::GetExtension($normalizado)
    if ($extensoesBinarias.Contains($ext)) { continue }

    $caminhoCompleto = Join-Path $RepositoryRoot $relativo
    if (-not (Test-Path -LiteralPath $caminhoCompleto -PathType Leaf)) { continue }

    $info = Get-Item -LiteralPath $caminhoCompleto
    if ($info.Length -gt $limiteBytes) {
        $achados.Add([pscustomobject]@{ Arquivo = $normalizado; Motivo = 'não conferido: arquivo textual acima de 2 MiB' })
        continue
    }

    try {
        $conteudo = [System.IO.File]::ReadAllText($caminhoCompleto)
    } catch {
        $achados.Add([pscustomobject]@{ Arquivo = $normalizado; Motivo = 'não conferido: falha ao ler arquivo' })
        continue
    }

    foreach ($regra in $padroesConteudo) {
        if ($conteudo -match $regra.Regex) {
            $achados.Add([pscustomobject]@{ Arquivo = $normalizado; Motivo = $regra.Motivo })
            break
        }
    }
}

if ($achados.Count -gt 0) {
    foreach ($item in $achados) {
        Write-Error "Proibido no repositório: $($item.Arquivo) ($($item.Motivo))" -ErrorAction Continue
    }
    exit 1
}

Write-Host "Verificação concluída: nenhum segredo ou arquivo proibido encontrado nos arquivos versionados."