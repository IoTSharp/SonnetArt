param(
    [string]$Url = "http://localhost:5131"
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$listenUrl = $Url.TrimEnd("/")
$healthUrl = "$listenUrl/health"

dotnet workload restore (Join-Path $repoRoot "src/SonnetArt/SonnetArt.csproj")

$browserJob = Start-Job -ScriptBlock {
    param([string]$TargetUrl, [string]$HealthUrl)

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Start-Process $TargetUrl
                return
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }
} -ArgumentList $listenUrl, $healthUrl

try {
    $env:ASPNETCORE_URLS = $listenUrl
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    dotnet run --project (Join-Path $repoRoot "src/SonnetHost/SonnetHost.csproj") --no-launch-profile
}
finally {
    if ($browserJob.State -eq "Running") {
        Stop-Job $browserJob
    }

    Remove-Job $browserJob -Force
}
