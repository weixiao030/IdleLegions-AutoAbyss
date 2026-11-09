$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
Push-Location (Join-Path $taskRoot 'ui')
try {
    if(-not (Test-Path 'node_modules')) { npm.cmd ci --no-audit --no-fund; if($LASTEXITCODE -ne 0){throw 'npm ci failed'} }
    npm.cmd run build
    if($LASTEXITCODE -ne 0){throw 'Frontend build failed'}
} finally { Pop-Location }
dotnet publish (Join-Path $taskRoot 'app') -c Release -r win-x64 --self-contained true -o (Join-Path $taskRoot 'release') --nologo -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE -ne 0){throw 'EXE publish failed'}
Get-FileHash -LiteralPath (Join-Path $taskRoot 'release\IdleLegions自动下井.exe') -Algorithm SHA256
