Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    throw 'The Windows .NET Framework64 v4.0.30319 C# compiler was not found.'
}

$src = Join-Path (Split-Path -Parent $PSScriptRoot) 'src'
$scratch = Join-Path $PSScriptRoot ('.check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $exe = Join-Path $scratch 'SessionCheck.exe'
    $sources = @(
        (Join-Path $PSScriptRoot 'Check.cs'),
        (Join-Path $src 'ProfileFiles.cs'),
        (Join-Path $src 'Session.cs')
    )
    & $csc /nologo /noconfig /langversion:5 /codepage:65001 /target:exe /reference:System.dll /reference:System.Core.dll "/out:$exe" @sources
    if ($LASTEXITCODE -ne 0) {
        throw "Check compilation failed (exit code $LASTEXITCODE)."
    }
    & $exe $scratch
    if ($LASTEXITCODE -ne 0) {
        throw "Synthetic bundle checks failed (exit code $LASTEXITCODE)."
    }
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
