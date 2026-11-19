param([string]$OutputPath = 'dist\Reze-Zapret.exe')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    throw 'The Windows .NET Framework64 v4.0.30319 C# compiler was not found.'
}

Push-Location -LiteralPath $PSScriptRoot
try {
    foreach ($asset in @('assets\reze-flowers.jpg', 'assets\reze-pixel-icon.ico')) {
        if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) { throw "Missing build asset: $asset" }
    }

    $sources = @(Get-ChildItem -LiteralPath 'src' -Filter '*.cs' -Recurse -File |
        Sort-Object FullName | ForEach-Object { $_.FullName })
    if ($sources.Count -eq 0) {
        throw 'No C# sources found in src.'
    }

    $outputFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
    New-Item -ItemType Directory -Path (Split-Path -Parent $outputFile) -Force | Out-Null
    $compilerArgs = @(
        '/nologo', '/noconfig', '/langversion:5', '/codepage:65001', '/target:winexe', '/optimize+',
        '/reference:System.dll', '/reference:System.Core.dll',
        '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.ServiceProcess.dll',
        '/reference:System.Net.Http.dll', '/reference:System.IO.Compression.dll', '/reference:System.Web.Extensions.dll',
        '/win32icon:assets\reze-pixel-icon.ico',
        '/resource:assets\reze-flowers.jpg,Reze.Banner', "/out:$outputFile"
    )
    & $csc @compilerArgs @sources
    if ($LASTEXITCODE -ne 0) {
        throw "C# compilation failed (exit code $LASTEXITCODE)."
    }
    Write-Output $outputFile
} finally {
    Pop-Location
}
