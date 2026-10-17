$dllPath = "C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command_Data\Managed\Assembly-CSharp.dll"
$asm = [System.Reflection.Assembly]::LoadFrom($dllPath)
$t = $asm.GetType("Entity.EntityIdBlueprintFilter")
Write-Output "=== Entity.EntityIdBlueprintFilter ==="
$flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance
foreach ($f in $t.GetFields($flags)) {
    Write-Output "  $($f.FieldType.ToString()) $($f.Name)"
}
