# SoundMaster local server — zero dependencies, Windows PowerShell 5.1 compatible.
# Serves this folder on http://localhost:9462 and opens your browser.
param([int]$Port = 9462, [switch]$NoBrowser)

$ErrorActionPreference = 'Stop'
$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootFull = (Get-Item $rootDir).FullName.TrimEnd('\')

$mime = @{
  '.html' = 'text/html; charset=utf-8'
  '.css'  = 'text/css; charset=utf-8'
  '.js'   = 'text/javascript; charset=utf-8'
  '.json' = 'application/json; charset=utf-8'
  '.svg'  = 'image/svg+xml'
  '.png'  = 'image/png'
  '.ico'  = 'image/x-icon'
  '.webm' = 'video/webm'
  '.mp3'  = 'audio/mpeg'
  '.wav'  = 'audio/wav'
  '.woff2'= 'font/woff2'
}

$listener = New-Object System.Net.HttpListener
$prefix = "http://localhost:$Port/"
$listener.Prefixes.Add($prefix)
try {
  $listener.Start()
} catch {
  # Probe whether an existing SoundMaster instance holds the port before claiming so.
  $alive = $false
  try {
    $probe = Invoke-WebRequest -Uri $prefix -UseBasicParsing -TimeoutSec 2
    if ($probe.StatusCode -eq 200 -and $probe.Content -match 'SoundMaster') { $alive = $true }
  } catch {}
  if ($alive) {
    Write-Host "SoundMaster is already running on port $Port. Opening browser." -ForegroundColor Yellow
    if (-not $NoBrowser) { Start-Process $prefix }
    exit 0
  }
  Write-Host "Could not start on port $Port : $($_.Exception.Message)" -ForegroundColor Red
  Write-Host "Something else may be using the port. Try: powershell -File server.ps1 -Port 9463" -ForegroundColor Yellow
  exit 1
}

Write-Host ""
Write-Host "  SoundMaster running at $prefix" -ForegroundColor Cyan
Write-Host "  Keep this window open. Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

if (-not $NoBrowser) { Start-Process $prefix }

try {
while ($listener.IsListening) {
  # Async accept polled in short slices so Ctrl+C is honored promptly
  # (a blocking GetContext() can't be interrupted in Windows PowerShell 5.1).
  $task = $listener.GetContextAsync()
  while (-not $task.Wait(250)) { }
  try {
    $ctx = $task.Result
  } catch {
    break
  }
  $req = $ctx.Request
  $res = $ctx.Response
  try {
    $path = [System.Uri]::UnescapeDataString($req.Url.AbsolutePath)
    if ($path -eq '/') { $path = '/index.html' }
    $candidate = Join-Path $rootFull ($path.TrimStart('/') -replace '/', '\')
    $full = $null
    try { $full = [System.IO.Path]::GetFullPath($candidate) } catch { $full = $null }

    # Path traversal guard: resolved path must stay inside the app folder.
    if ($null -eq $full -or -not $full.StartsWith($rootFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
      $res.StatusCode = 403
    } elseif (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
      $res.StatusCode = 404
    } else {
      $ext = [System.IO.Path]::GetExtension($full).ToLowerInvariant()
      $type = $mime[$ext]
      if (-not $type) { $type = 'application/octet-stream' }
      $bytes = [System.IO.File]::ReadAllBytes($full)
      $res.ContentType = $type
      $res.Headers.Add('Cache-Control', 'no-cache')
      $res.ContentLength64 = $bytes.Length
      $res.OutputStream.Write($bytes, 0, $bytes.Length)
    }
  } catch {
    try { $res.StatusCode = 500 } catch {}
  } finally {
    try { $res.OutputStream.Close() } catch {}
  }
}
} finally {
  try { $listener.Stop(); $listener.Close() } catch {}
}
