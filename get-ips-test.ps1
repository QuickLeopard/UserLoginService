# Script to test GetAllUserIPs functionality with multiple requests
Write-Host "Starting GetAllUserIPs Test"
Write-Host "============================="

# First round - Initial requests for different users (should hit database)
Write-Host "`nRound 1: Initial requests (DB access)`n"
for ($i = 1; $i -le 5; $i++) {
    Write-Host "Testing User ID: $i"
    $startTime = Get-Date
    docker-compose run --rm userloginclient get-ips --user $i
    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalMilliseconds
    Write-Host "Response time: $duration ms`n"
}

# Wait a moment
Start-Sleep -Seconds 2

# Second round - Same users (should hit cache)
Write-Host "`nRound 2: Repeated requests (Cache hits)`n"
for ($i = 1; $i -le 5; $i++) {
    Write-Host "Testing User ID: $i (Cached)"
    $startTime = Get-Date
    docker-compose run --rm userloginclient get-ips --user $i
    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalMilliseconds
    Write-Host "Response time: $duration ms`n"
}

# Parallel testing - Hit with multiple parallel requests for the same user
Write-Host "`nRound 3: Parallel requests for the same user`n"
Write-Host "Running 10 parallel requests for User ID: 1"
$jobs = @()
for ($i = 1; $i -le 10; $i++) {
    $jobs += Start-Job -ScriptBlock {
        docker-compose run --rm userloginclient get-ips --user 1
    }
}

# Wait for all jobs to complete
$jobs | Wait-Job | Receive-Job

Write-Host "`nTest completed"
