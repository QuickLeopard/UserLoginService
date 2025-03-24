# PowerShell script to generate HTML report from stress test logs

param (
    [string]$LogDir = ".\logs",
    [string]$OutputFile = ".\Reports\stress-test-report.html"
)

# Ensure Reports directory exists
if (!(Test-Path ".\Reports")) {
    New-Item -ItemType Directory -Path ".\Reports" -Force | Out-Null
}

# Create timestamp for the report
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

# Get all log files in the specified directory
$logFiles = Get-ChildItem -Path $LogDir -Filter "load-test-*.log" | Sort-Object Name

if ($logFiles.Count -eq 0) {
    Write-Host "No log files found in $LogDir"
    exit
}

# Define test phases
$testPhases = @(
    "100k Requests",
    "10k Requests",
    "Mixed IPv4/IPv6 Addresses Load Test",
    "High Concurrency Test",
    "Burst Test (Zero Delay)",
    "High Volume Test (5000 Requests)",
    "50k Requests" 
)

# HTML header with styles
$html = @"
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>HighLoadUserLoginService Stress Test Results</title>
    <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    <style>
        body {
            font-family: Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 1200px;
            margin: 0 auto;
            padding: 20px;
        }
        
        .header {
            text-align: center;
            margin-bottom: 30px;
            padding: 20px;
            background: #f5f5f5;
            border-radius: 5px;
        }
        
        h1 {
            color: #2c3e50;
        }
        
        h2 {
            color: #3498db;
            border-bottom: 2px solid #ecf0f1;
            padding-bottom: 10px;
            margin-top: 30px;
        }
        
        h3 {
            color: #2980b9;
        }
        
        .summary {
            background: #ecf0f1;
            padding: 20px;
            border-radius: 5px;
            margin-bottom: 20px;
        }
        
        .metrics {
            display: flex;
            flex-wrap: wrap;
            gap: 15px;
            margin-bottom: 20px;
        }
        
        .metric-card {
            background: white;
            border: 1px solid #ddd;
            border-radius: 5px;
            padding: 15px;
            width: calc(25% - 15px);
            box-sizing: border-box;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
        }
        
        .metric-card h3 {
            margin-top: 0;
            font-size: 14px;
            color: #7f8c8d;
        }
        
        .metric-value {
            font-size: 24px;
            font-weight: bold;
            margin: 10px 0;
            color: #2c3e50;
        }
        
        .metric-value.success {
            color: #27ae60;
        }
        
        .metric-value.warning {
            color: #f39c12;
        }
        
        .metric-value.error {
            color: #e74c3c;
        }
        
        .metric-card p {
            margin: 0;
            font-size: 12px;
            color: #95a5a6;
        }
        
        .chart-container {
            height: 300px;
            margin-bottom: 40px;
        }
        
        .test-phase {
            background: #f9f9f9;
            padding: 20px;
            border-radius: 5px;
            margin-bottom: 30px;
            border-left: 5px solid #3498db;
        }
        
        .response-stats {
            display: flex;
            gap: 15px;
            margin-bottom: 20px;
        }
        
        .response-stat {
            background: white;
            border: 1px solid #ddd;
            padding: 10px;
            border-radius: 5px;
            flex: 1;
            text-align: center;
        }
        
        .response-label {
            font-size: 12px;
            color: #7f8c8d;
        }
        
        .response-value {
            font-size: 18px;
            font-weight: bold;
            margin: 5px 0;
        }
        
        @media (max-width: 768px) {
            .metric-card {
                width: calc(50% - 15px);
            }
            
            .response-stats {
                flex-direction: column;
            }
        }
    </style>
</head>
<body>
    <div class="header">
        <h1>HighLoadUserLoginService Stress Test Results</h1>
        <p>Report generated: $timestamp</p>
    </div>
"@

# Initialize variables for overall metrics
$totalRequests = 0
$totalSuccessful = 0
$maxRPS = 0
$totalResponseTime = 0
$responseTimeCount = 0

# Process each log file and store the data
$phaseData = @()

for ($i = 0; $i -lt $logFiles.Count; $i++) {
    Write-Host "Processing log file: $($logFiles[$i].Name)"
    
    $log = Get-Content $logFiles[$i].FullName
    
    # Extract data from log files
    $requests = ($log | Select-String -Pattern "Total requests: (\d+)" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [int]
    $successful = ($log | Select-String -Pattern "Successful: (\d+)" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [int]
    $failed = ($log | Select-String -Pattern "Failed: (\d+)" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [int]
    $duration = ($log | Select-String -Pattern "Duration: ([\d\.]+) seconds" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [double]
    $rps = ($log | Select-String -Pattern "Requests per second: ([\d\.]+)" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [double]
    
    Write-Host "Total requests: $requests"

    # Extract parallel connections with a more reliable pattern
    $parallelConnections = 0
    $parallelMatch = $log | Select-String -Pattern "Starting load test with \d+ requests using (\d+) parallel tasks"
    if ($parallelMatch) {
        $parallelConnections = $parallelMatch[0].Matches.Groups[1].Value -as [int]
    }
    
    # If parallel connections is still 0, use the phase information to set default values
    if ($parallelConnections -eq 0) {
        if ($i -eq 1) { # High Concurrency Test
            $parallelConnections = 50
        } elseif ($i -eq 0 -or $i -eq 2 -or $i -eq 3) { # Standard tests use 10 by default
            $parallelConnections = 10
        } elseif ($i -eq 4) { # 10k test
            $parallelConnections = 20
        } elseif ($i -eq 5) { # 50k test
            $parallelConnections = 50
        } elseif ($i -eq 6) { # 100k test
            $parallelConnections = 100
        }
    }
    
    $avg = ($log | Select-String -Pattern "Average: ([\d\.]+) ms" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [double]
    $median = ($log | Select-String -Pattern "Median \(P50\): ([\d\.]+) ms" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [double]
    $p90 = ($log | Select-String -Pattern "P90: ([\d\.]+) ms" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [double]
    $p99 = ($log | Select-String -Pattern "P99: ([\d\.]+) ms" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [double]
    $min = ($log | Select-String -Pattern "Min: ([\d\.]+) ms" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [double]
    $max = ($log | Select-String -Pattern "Max: ([\d\.]+) ms" | ForEach-Object { $_.Matches.Groups[1].Value }) -as [double]
    
    # Make sure response time data is properly protected against null/zero values
    if ($null -eq $avg) { $avg = 0 }
    if ($null -eq $median) { $median = 0 }
    if ($null -eq $p90) { $p90 = 0 }
    if ($null -eq $p99) { $p99 = 0 }
    if ($null -eq $min) { $min = 0 }
    if ($null -eq $max) { $max = 0 }
    
    # Only count valid response times
    if ($avg -gt 0) { 
        $totalResponseTime += $avg
        $responseTimeCount++
    }
    
    # Distribution data (for chart)
    $distribution = @()
    $log | Select-String -Pattern "([\d]+)-([\d]+)ms\s+:\s+(\d+) \(\s*([\d\.]+)%\)" | ForEach-Object { 
        $rangeStr = "$($_.Matches.Groups[1].Value)-$($_.Matches.Groups[2].Value)ms"
        $countStr = $_.Matches.Groups[3].Value
        $percentageStr = $_.Matches.Groups[4].Value
        $distribution += "${rangeStr}: $countStr ($percentageStr%)"
    }
    
    # Accumulate totals
    $totalRequests += $requests
    $totalSuccessful += $successful
    
    if ($rps -gt $maxRPS) {
        $maxRPS = $rps
    }
    
    # Store phase data in a hashtable
    $phaseItem = @{
        phase = if ($i -lt $testPhases.Count) { $testPhases[$i] } else { "Additional Test $($i+1)" }
        requests = $requests
        successful = $successful
        failed = $failed
        duration = $duration
        rps = $rps
        parallelConnections = $parallelConnections
        avg = $avg
        median = $median
        p90 = $p90
        p99 = $p99
        min = $min
        max = $max
        distribution = $distribution
    }
    
    # Add the hashtable to the array
    $phaseData += $phaseItem
}

# Sort phase data by number of requests (from smallest to largest)
# For special named tests, assign a priority based on their expected size
#$phaseData = $phaseData | ForEach-Object {
#    $priority = switch ($_.phase) {
#        "Mixed IPv4/IPv6 Addresses Load Test" { 1000 }
#        "High Concurrency Test" { 2000 }
#        "Burst Test (Zero Delay)" { 3000 }
#        "High Volume Test (5000 Requests)" { 5000 }
#        "10k Requests" { 10000 }
#        "50k Requests" { 50000 }
#        "100k Requests" { 100000 }
#        default { $_.requests }
#    }
    
    # Add the priority as a property for sorting
#    $_ | Add-Member -NotePropertyName "SortPriority" -NotePropertyValue $priority -PassThru
#} | Sort-Object -Property SortPriority

# Calculate overall metrics for summary section
# Calculate success rate safely
$successRate = if ($totalRequests -gt 0) { ($totalSuccessful / $totalRequests) * 100 } else { 0 }
$avgResponseTimeValue = if ($responseTimeCount -gt 0) { $totalResponseTime / $responseTimeCount } else { 0 }

# Add overall metrics to summary
$html += @"
    <div class="summary">
        <h2>Summary</h2>
        <div class="metrics">
            <div class="metric-card">
                <h3>Total Requests</h3>
                <div class="metric-value">$totalRequests</div>
                <p>Total login requests processed</p>
            </div>
            <div class="metric-card">
                <h3>Success Rate</h3>
                <div class="metric-value $( if ($totalRequests -gt 0 -and $totalRequests - $totalSuccessful -eq 0) { "success" } elseif ($totalRequests -gt 0 -and ($totalRequests - $totalSuccessful) / $totalRequests -lt 0.05) { "warning" } else { "error" } )">
                    $([math]::Round($successRate, 2))%
                </div>
                <p>Percentage of successful requests</p>
            </div>
            <div class="metric-card">
                <h3>Max Throughput</h3>
                <div class="metric-value">$([math]::Round($maxRPS, 2))</div>
                <p>Requests per second</p>
            </div>
            <div class="metric-card">
                <h3>Avg Response Time</h3>
                <div class="metric-value">$([math]::Round($avgResponseTimeValue, 2)) ms</div>
                <p>Average response time</p>
            </div>
        </div>
    </div>

    <h2>IPv4 and IPv6 Support Performance</h2>
    <p>These tests evaluate the performance of the UserLoginService with both IPv4 and IPv6 addresses under various load conditions.</p>
    
    <!-- Overall charts -->
    <div class="chart-container">
        <canvas id="performanceChart"></canvas>
    </div>
"@

# Add detailed section for each test phase
for ($i = 0; $i -lt $phaseData.Count; $i++) {
    $phase = $phaseData[$i]

    Write-Host "Phase requests: $($phase.phase)"

    Write-Host "Phase requests: $($phase.requests)"
    
    $html += @"
    <div class="test-phase">
        <h2>$($phase.phase)</h2>
        <div class="metrics">
            <div class="metric-card">
                <h3>Requests</h3>
                <div class="metric-value">$($phase.requests)</div>
            </div>
            <div class="metric-card">
                <h3>Success Rate</h3>
                <div class="metric-value $( if ($phase.failed -eq 0) { "success" } elseif ($phase.failed -lt 5) { "warning" } else { "error" } )">
                    $( if ($phase.requests -gt 0) { [math]::Round(($phase.successful / $phase.requests) * 100, 2) } else { 0 } )%
                </div>
            </div>
            <div class="metric-card">
                <h3>Throughput</h3>
                <div class="metric-value">$([math]::Round($phase.rps, 2))</div>
                <p>Requests per second</p>
            </div>
            <div class="metric-card">
                <h3>Duration</h3>
                <div class="metric-value">$([math]::Round($phase.duration, 2))s</div>
            </div>
            <div class="metric-card">
                <h3>Parallel Connections</h3>
                <div class="metric-value">$($phase.parallelConnections)</div>
                <p>Concurrent tasks</p>
            </div>
        </div>
        
        <h3>Response Time Statistics</h3>
        <div class="response-stats">
            <div class="response-stat">
                <div class="response-label">Average</div>
                <div class="response-value">$([math]::Round($phase.avg, 2)) ms</div>
            </div>
            <div class="response-stat">
                <div class="response-label">Median (P50)</div>
                <div class="response-value">$([math]::Round($phase.median, 2)) ms</div>
            </div>
            <div class="response-stat">
                <div class="response-label">P90</div>
                <div class="response-value">$([math]::Round($phase.p90, 2)) ms</div>
            </div>
            <div class="response-stat">
                <div class="response-label">P99</div>
                <div class="response-value">$([math]::Round($phase.p99, 2)) ms</div>
            </div>
            <div class="response-stat">
                <div class="response-label">Min</div>
                <div class="response-value">$([math]::Round($phase.min, 2)) ms</div>
            </div>
            <div class="response-stat">
                <div class="response-label">Max</div>
                <div class="response-value">$([math]::Round($phase.max, 2)) ms</div>
            </div>
        </div>
        
        <h3>Response Time Distribution</h3>
        <div class="chart-container">
            <canvas id="distributionChart$i"></canvas>
        </div>
    </div>
"@
}

# Create chart labels and data arrays for JavaScript
$phaseLabels = @()
$phaseRps = @()
$phaseAvg = @()
$phaseSuccessRates = @()

foreach ($phase in $phaseData) {
    $phaseLabels += "'$($phase.phase)'"
    $phaseRps += $phase.rps
    $phaseAvg += $phase.avg
    
    # Calculate success rate safely
    if ($phase.requests -gt 0) {
        $phaseSuccessRates += [math]::Round(($phase.successful / $phase.requests) * 100, 2)
    } else {
        $phaseSuccessRates += 0
    }
}

# Add JavaScript for charts
$html += @"
    <script>
        // Performance comparison chart
        const ctx = document.getElementById('performanceChart').getContext('2d');
        const chart = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: [$($phaseLabels -join ', ')],
                datasets: [
                    {
                        label: 'Throughput (req/s)',
                        data: [$($phaseRps -join ', ')],
                        backgroundColor: 'rgba(54, 162, 235, 0.5)',
                        borderColor: 'rgba(54, 162, 235, 1)',
                        borderWidth: 1,
                        yAxisID: 'y'
                    },
                    {
                        label: 'Avg Response Time (ms)',
                        data: [$($phaseAvg -join ', ')],
                        backgroundColor: 'rgba(255, 99, 132, 0.5)',
                        borderColor: 'rgba(255, 99, 132, 1)',
                        borderWidth: 1,
                        yAxisID: 'y1'
                    },
                    {
                        label: 'Success Rate (%)',
                        data: [$($phaseSuccessRates -join ', ')],
                        backgroundColor: 'rgba(75, 192, 192, 0.5)',
                        borderColor: 'rgba(75, 192, 192, 1)',
                        borderWidth: 1,
                        yAxisID: 'y2'
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    y: {
                        type: 'linear',
                        display: true,
                        position: 'left',
                        title: {
                            display: true,
                            text: 'Throughput (req/s)'
                        }
                    },
                    y1: {
                        type: 'linear',
                        display: true,
                        position: 'right',
                        grid: {
                            drawOnChartArea: false,
                        },
                        title: {
                            display: true,
                            text: 'Response Time (ms)'
                        }
                    },
                    y2: {
                        type: 'linear',
                        display: true,
                        position: 'right',
                        grid: {
                            drawOnChartArea: false,
                        },
                        min: 0,
                        max: 100,
                        title: {
                            display: true,
                            text: 'Success Rate (%)'
                        }
                    }
                }
            }
        });
"@

# Add distribution charts for each phase
for ($i = 0; $i -lt $phaseData.Count; $i++) {
    $phase = $phaseData[$i]
    
    # Skip if there's no distribution data
    if ($null -eq $phase.distribution -or $phase.distribution.Count -eq 0) {
        continue
    }
    
    $distributionRanges = @()
    $distributionCounts = @()
    
    # Parse distribution data safely
    foreach ($item in $phase.distribution) {
        if ($item -match "(\d+)-(\d+)ms: (\d+)") {
            $min = [int]$matches[1]
            $max = [int]$matches[2]
            $count = [int]$matches[3]
            
            $distributionRanges += "'$min-$max ms'"
            $distributionCounts += $count
        }
    }
    
    # Only create chart if we have data
    if ($distributionRanges.Count -gt 0) {
        $html += @"
        
        // Distribution chart for phase $i
        const distributionCtx$i = document.getElementById('distributionChart$i').getContext('2d');
        const distributionChart$i = new Chart(distributionCtx$i, {
            type: 'bar',
            data: {
                labels: [$($distributionRanges -join ', ')],
                datasets: [{
                    label: 'Number of requests',
                    data: [$($distributionCounts -join ', ')],
                    backgroundColor: 'rgba(75, 192, 192, 0.5)',
                    borderColor: 'rgba(75, 192, 192, 1)',
                    borderWidth: 1
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    y: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: 'Count'
                        }
                    }
                }
            }
        });
"@
    }
}

$html += @"
    </script>
</body>
</html>
"@

# Write the HTML to the output file
$html | Out-File -FilePath $OutputFile -Encoding utf8

Write-Host "Report generated successfully at $OutputFile"

# Open the report in the default browser if on Windows
if ($IsWindows -or ($null -eq $IsWindows)) {
    Start-Process $OutputFile
}
