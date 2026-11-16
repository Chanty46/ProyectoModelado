# Pide las claves por consola (ocultas) y las escribe en backend/.env y frontend/.env.
# Las claves nunca se imprimen. Deja en blanco para conservar el valor actual.
$root = Split-Path -Parent $PSScriptRoot
$backendEnv = Join-Path $root "backend\.env"
$frontendEnv = Join-Path $root "frontend\.env"

foreach ($pair in @(@($backendEnv, "backend\.env.example"), @($frontendEnv, "frontend\.env.example"))) {
    if (-not (Test-Path $pair[0])) { Copy-Item (Join-Path $root $pair[1]) $pair[0] }
}

function Read-Secret([string]$prompt) {
    $secure = Read-Host -Prompt $prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr).Trim() }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Set-EnvValue([string]$file, [string]$key, [string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return }
    $lines = [System.Collections.Generic.List[string]](Get-Content $file)
    $idx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^\s*$key\s*=") { $idx = $i; break } }
    if ($idx -ge 0) { $lines[$idx] = "$key=$value" } else { $lines.Add("$key=$value") }
    [System.IO.File]::WriteAllLines($file, $lines, (New-Object System.Text.UTF8Encoding($false)))
}

$secret = Read-Secret "SUPABASE_SECRET_KEY (sb_secret_...)"
$telegram = Read-Secret "TELEGRAM_BOT_TOKEN (Enter para omitir)"
$publishable = Read-Secret "VITE_SUPABASE_PUBLISHABLE_KEY (sb_publishable_...)"

Set-EnvValue $backendEnv "SUPABASE_SECRET_KEY" $secret
Set-EnvValue $backendEnv "TELEGRAM_BOT_TOKEN" $telegram
Set-EnvValue $frontendEnv "VITE_SUPABASE_PUBLISHABLE_KEY" $publishable

$ok1 = [bool](Select-String -Path $backendEnv -Pattern '^SUPABASE_SECRET_KEY=sb_secret_\S+' -Quiet)
$ok2 = [bool](Select-String -Path $frontendEnv -Pattern '^VITE_SUPABASE_PUBLISHABLE_KEY=sb_publishable_\S+' -Quiet)
Write-Host "backend secret key guardada: $ok1"
Write-Host "frontend publishable key guardada: $ok2"
