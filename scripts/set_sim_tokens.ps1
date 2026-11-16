# Guarda en simulators/devices.local.json (ignorado por Git) el token que la web
# muestra UNA sola vez al emparejar cada dispositivo simulado.
# Enter vacío = conservar lo que ya había para ese dispositivo.
$root = Split-Path -Parent $PSScriptRoot
$file = Join-Path $root "simulators\devices.local.json"

$current = @{}
if (Test-Path $file) {
    $json = Get-Content -Raw -Encoding UTF8 $file | ConvertFrom-Json
    foreach ($p in $json.PSObject.Properties) { $current[$p.Name] = @{ device_id = $p.Value.device_id; token = $p.Value.token } }
}

$devices = [ordered]@{
    central = "demo-central-01"; colchon = "demo-colchon-01"; movil = "demo-movil-01";
    biberon = "demo-biberon-01"; juguete = "demo-juguete-01"
}

foreach ($kind in $devices.Keys) {
    $token = (Read-Host "Token de $($devices[$kind]) ($kind) [Enter = omitir]").Trim()
    if ($token) { $current[$kind] = @{ device_id = $devices[$kind]; token = $token } }
}

$current | ConvertTo-Json | Set-Content -Encoding UTF8 $file
Write-Host "Guardados $($current.Count) dispositivo(s): $($current.Keys -join ', ')"
