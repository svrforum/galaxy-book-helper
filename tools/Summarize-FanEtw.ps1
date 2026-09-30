param([string]$RunDirectory)
$ErrorActionPreference = 'Stop'
if (!$RunDirectory) {
    $RunDirectory = (Get-Content (Join-Path $PSScriptRoot '..\artifacts\fan-etw-latest.txt') -Raw).Trim()
}
[xml]$trace = Get-Content (Join-Path $RunDirectory 'events.xml') -Raw
$events = @($trace.Events.Event)
$decoded = @($events | Where-Object { $_.RenderingInfo.Task })
$candidates = @($decoded | Where-Object {
    # Match event metadata, never the ETL filename (which contains fan).
    $_.RenderingInfo.Task -match 'fan|rpm|cooling' -or
    @($_.EventData.Data | Where-Object { $_.Name -match 'fan|rpm|cooling' }).Count -gt 0
} | ForEach-Object {
    [pscustomobject]@{
        Task = [string]$_.RenderingInfo.Task
        Provider = [string]$_.System.Provider.Name
        Fields = @($_.EventData.Data | ForEach-Object {
            [pscustomobject]@{Name=[string]$_.Name; Value=[string]$_.'#text'}
        })
    }
})
$report = [pscustomobject]@{
    TotalEvents = $events.Count
    NamedEvents = $decoded.Count
    Tasks = @($decoded | Group-Object { [string]$_.RenderingInfo.Task } | ForEach-Object {
        [pscustomobject]@{Name=$_.Name; Count=$_.Count}
    })
    FanOrCoolingCandidates = $candidates
    Interpretation = 'Candidates require semantic review; no candidates in a short runtime trace does not establish lack of firmware support. This report does not establish RPM control.'
}
$json = $report | ConvertTo-Json -Depth 8
$json | Set-Content (Join-Path $RunDirectory 'fan-analysis.json') -Encoding UTF8
$json
