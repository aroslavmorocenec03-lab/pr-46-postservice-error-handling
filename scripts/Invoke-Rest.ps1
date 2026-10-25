param(
    [string]$File = "$PSScriptRoot/../requests/postservice.rest",
    [string]$Report = "$PSScriptRoot/../reports/rest-results.txt"
)

# Невеликий виконавець саме доданих .rest-файлів; потрібен PowerShell 7.
$ErrorActionPreference = 'Stop'
$restText = Get-Content -LiteralPath $File -Raw
$baseMatch = [regex]::Match($restText, '(?m)^@baseUrl\s*=\s*(\S+)')
if (-not $baseMatch.Success) { throw 'У .rest немає @baseUrl.' }
$baseAddress = $baseMatch.Groups[1].Value
$reportLines = [Collections.Generic.List[string]]::new()
$reportLines.Add("Файл: $([IO.Path]::GetFileName($File))")
$reportLines.Add("Час: $([DateTimeOffset]::Now.ToString('O'))")

foreach ($block in [regex]::Split($restText, '(?m)^###')) {
    $lines = $block -split '\r?\n'
    $requestIndex = -1
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^(GET|POST|PUT|DELETE)\s+(\S+)') { $requestIndex = $i; break }
    }
    if ($requestIndex -lt 0) { continue }
    $requestMatch = [regex]::Match($lines[$requestIndex], '^(GET|POST|PUT|DELETE)\s+(\S+)')
    $method = $requestMatch.Groups[1].Value
    $uri = $requestMatch.Groups[2].Value.Replace('{{baseUrl}}', $baseAddress)
    $headers = @{}
    $bodyStart = $lines.Length
    for ($i = $requestIndex + 1; $i -lt $lines.Length; $i++) {
        if ([string]::IsNullOrWhiteSpace($lines[$i])) { $bodyStart = $i + 1; break }
        if ($lines[$i] -match '^([^:]+):\s*(.*)$') { $headers[$Matches[1]] = $Matches[2] }
    }
    $body = if ($bodyStart -lt $lines.Length) { ($lines[$bodyStart..($lines.Length - 1)] -join "`n").Trim() } else { '' }
    $reportLines.Add("`n$method $uri")
    try {
        $parameters = @{ Uri = $uri; Method = $method; Headers = $headers; TimeoutSec = 3; SkipHttpErrorCheck = $true }
        if ($body.Length -gt 0) { $parameters.Body = [Text.Encoding]::UTF8.GetBytes($body) }
        $response = Invoke-WebRequest @parameters
        $reportLines.Add("HTTP $([int]$response.StatusCode)")
        if ($response.Content) {
            $responseText = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
            $reportLines.Add($responseText)
        }
    }
    catch [System.Net.Http.HttpRequestException] { $reportLines.Add("NETWORK: $($_.Exception.Message)") }
    catch [System.Threading.Tasks.TaskCanceledException] { $reportLines.Add("TIMEOUT: $($_.Exception.Message)") }
}
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Report))) -Force | Out-Null
$reportLines | Set-Content -LiteralPath $Report -Encoding utf8
$reportLines
