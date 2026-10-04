param(
    [string]$FirmwareRoot = 'D:\Workspace\KeyboardFirmware',
    [string]$Distribution = 'Ubuntu-24.04'
)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$firmware = (Resolve-Path -LiteralPath $FirmwareRoot).Path
$output = Join-Path $workspace '.firmware-stage/end-to-end'
dotnet run --project (Join-Path $PSScriptRoot 'HostLed.EndToEnd.csproj') -- $firmware $output
if ($LASTEXITCODE -ne 0) { throw 'End-to-end trace generation failed' }

function LinuxPath([string]$Path) {
    $result = & wsl -d $Distribution -- wslpath -a ($Path.Replace('\', '/'))
    if ($LASTEXITCODE -ne 0) { throw "wslpath failed: $Path" }
    return ($result -join "`n").Trim()
}
$scriptPath = LinuxPath (Join-Path $PSScriptRoot 'run.sh')
$firmwarePath = LinuxPath $firmware
$outputPath = LinuxPath $output
& wsl -d $Distribution -- bash $scriptPath $firmwarePath $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Firmware replay failed; see scenarios.txt for record ranges' }
$custom = Join-Path $firmware 'keyboards/keychron/q6_pro/ansi_encoder/keymaps/custom'
@('host_led_protocol.c', 'host_led_protocol.h', 'host_led_qmk.c', 'tests/qmk_stub.h', 'tests/led_map.h') |
    ForEach-Object { Get-FileHash -LiteralPath (Join-Path $custom $_) -Algorithm SHA256 } |
    Select-Object Path, Hash | ConvertTo-Json | Set-Content (Join-Path $output 'firmware-sources.json') -Encoding utf8
Write-Host "PASS: artifacts in $output"
