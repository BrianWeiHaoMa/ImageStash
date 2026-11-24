<#
.SYNOPSIS
A built MSI's tables, read through the Windows Installer object without installing the package, dot-sourced by the
scripts that judge an MSI (check-installer.ps1, check-installer-start.ps1). $installer is that object; Com calls one of its
members, or a member of what it returns.
#>
$installer = New-Object -ComObject WindowsInstaller.Installer

function Com($target, [string] $member, [string] $kind, [object[]] $arguments) {
    $target.GetType().InvokeMember($member, $kind, $null, $target, $arguments)
}

# The rows <query> selects from the package, each written as one array of its columns as strings, in the query's
# order; collect them with @(), which keeps a single row whole.
function MsiRows([string] $msi, [string] $query) {
    $database = Com $installer 'OpenDatabase' 'InvokeMethod' @($msi, [object] 0)
    $view = Com $database 'OpenView' 'InvokeMethod' @($query)
    $null = Com $view 'Execute' 'InvokeMethod' @()
    $rows = New-Object System.Collections.Generic.List[object]
    while ($record = Com $view 'Fetch' 'InvokeMethod' @()) {
        $count = Com $record 'FieldCount' 'GetProperty' @()
        # A plain loop: a pipeline's $_ reaches COM wrapped, which StringData refuses as a type mismatch.
        $columns = New-Object string[] $count
        for ($field = 1; $field -le $count; $field++) { $columns[$field - 1] = Com $record 'StringData' 'GetProperty' @([object] $field) }
        $rows.Add($columns)
    }
    $null = Com $view 'Close' 'InvokeMethod' @()
    $rows.ToArray()
}

# A value of the MSI's Property table, read from the package itself; $null when the table has no such property.
function MsiProperty([string] $msi, [string] $property) {
    $rows = @(MsiRows $msi "SELECT Value FROM Property WHERE Property = '$property'")
    if ($rows.Count -gt 0) { $rows[0][0] } else { $null }
}
