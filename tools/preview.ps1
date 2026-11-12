param([int]$Port = 8765)
$ErrorActionPreference = 'Stop'
$previewRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Local: http://localhost:$Port/mockup/"
Write-Host 'Stop with Ctrl+C. Serves this repository on localhost only.'
try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            $relative = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath).TrimStart('/')
            if (-not $relative) { $relative = 'mockup/index.html' }
            if ($relative.EndsWith('/')) { $relative += 'index.html' }
            $target = [System.IO.Path]::GetFullPath((Join-Path $previewRoot $relative))
            if (-not $target.StartsWith($previewRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $relative -match '(^|[\\/])\.') {
                $context.Response.StatusCode = 403
            } elseif (-not [System.IO.File]::Exists($target)) {
                $context.Response.StatusCode = 404
            } else {
                $types = @{'.html'='text/html';'.css'='text/css';'.js'='text/javascript';'.json'='application/json';'.md'='text/plain'}
                $extension = [System.IO.Path]::GetExtension($target)
                $context.Response.ContentType = $(if ($types.ContainsKey($extension)) { $types[$extension] + '; charset=utf-8' } else { 'application/octet-stream' })
                $bytes = [System.IO.File]::ReadAllBytes($target)
                $context.Response.ContentLength64 = $bytes.Length
                $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            }
        } catch {
            $context.Response.StatusCode = 500
        } finally {
            $context.Response.Close()
        }
    }
} finally {
    $listener.Stop()
    $listener.Close()
}
