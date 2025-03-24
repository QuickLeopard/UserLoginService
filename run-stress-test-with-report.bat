@echo off
echo === UserLoginService IPv4/IPv6 Stress Test with Report Generation ===
echo.

REM Set timestamp for the report
for /f "tokens=2-4 delims=/ " %%a in ('date /t') do (set DATE=%%c-%%a-%%b)
for /f "tokens=1-2 delims=: " %%a in ('time /t') do (set TIME=%%a%%b)
set TIMESTAMP=%DATE%_%TIME%

echo Running stress tests - %TIMESTAMP%
echo This will run a series of tests on the UserLoginService with both IPv4 and IPv6 addresses
echo.

REM Create logs directory if it doesn't exist
if not exist ".\logs" mkdir ".\logs"

REM Create Reports directory if it doesn't exist
if not exist ".\Reports" mkdir ".\Reports"

REM Ensure the logs directory is empty before starting
del /Q .\logs\*.*

REM Run the standard stress tests
echo Executing standard stress tests...
call stress-test-userlogin.bat

REM Run high volume tests
echo.
echo Executing 10k, 50k, and 100k request stress tests...
echo.

REM 10k Requests Test
echo === Starting 10k Requests Test ===
echo [Test Phase: 10k Requests] > .\logs\load-test-10k.log
echo Starting load test with 10000 requests using 20 parallel tasks >> .\logs\load-test-10k.log
docker-compose run --rm userloginclient load-test --count=10000 --parallel=20 --ips=9999 --delay=5 >> .\logs\load-test-10k.log 2>&1

REM 50k Requests Test 
echo === Starting 50k Requests Test ===
echo [Test Phase: 50k Requests] > .\logs\load-test-50k.log
echo Starting load test with 50000 requests using 50 parallel tasks >> .\logs\load-test-50k.log
docker-compose run --rm userloginclient load-test --count=50000 --parallel=50 --ips=16385 --delay=1 >> .\logs\load-test-50k.log 2>&1

REM 100k Requests Test
echo === Starting 100k Requests Test ===
echo [Test Phase: 100k Requests] > .\logs\load-test-100k.log
echo Starting load test with 100000 requests using 100 parallel tasks >> .\logs\load-test-100k.log
docker-compose run --rm userloginclient load-test --count=100000 --parallel=100 --ips=65535 --delay=0 >> .\logs\load-test-100k.log 2>&1

REM Save all test output to a consolidated log file
echo Consolidating test output...
copy .\logs\*.log .\Reports\stress-test-output-%TIMESTAMP%.log

echo.
echo Stress tests completed, generating HTML report...
echo.

REM Generate the HTML report with PowerShell
powershell -ExecutionPolicy Bypass -File .\generate-stress-test-report.ps1 -OutputFile ".\Reports\stress-test-report-%TIMESTAMP%.html"

echo.
echo === Test Process Complete ===
echo.
echo HTML report has been generated at: Reports\stress-test-report-%TIMESTAMP%.html
echo.
echo You can view the report by opening it in your web browser.
echo.

REM Open the report in the default web browser
start "" ".\Reports\stress-test-report-%TIMESTAMP%.html"
