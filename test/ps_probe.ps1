$ErrorActionPreference = 'Stop'
Write-Output "probe start $(Get-Date -Format HH:mm:ss)"
try {
    $app = [Runtime.InteropServices.Marshal]::GetActiveObject("Photoshop.Application")
    Write-Output "connected"
    $js = 'function f(){var d=app.activeDocument;var l=d.activeLayer;return JSON.stringify({ok:1,k:l.kind});}f();'
    $r = $app.DoJavaScript($js)
    Write-Output ("result: " + $r)
} catch {
    Write-Output ("ERR: " + $_.Exception.Message)
}
Write-Output "probe end $(Get-Date -Format HH:mm:ss)"
