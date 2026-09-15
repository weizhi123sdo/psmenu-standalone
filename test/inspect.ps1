$a = [Reflection.Assembly]::LoadFrom('E:\code\psmenu\lib\FontAwesomeIconsWpf.dll')
$enums = $a.GetExportedTypes() | Where-Object { $_.IsEnum }
foreach ($e in $enums) { Write-Output ("ENUM " + $e.FullName) }
$types = $a.GetExportedTypes() | Where-Object { $_.FullName -match 'SvgAwesome|ImageAwesome' }
foreach ($t in $types) { Write-Output ("TYPE " + $t.FullName) }
