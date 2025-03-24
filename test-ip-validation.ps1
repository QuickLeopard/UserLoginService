# Test script for IP address validation
Write-Host "=== Testing IP address validation ===" -ForegroundColor Cyan

# Test 1: Valid IP address - should succeed
Write-Host "`n[Test 1] Valid IP address (192.168.1.1)" -ForegroundColor Green
docker-compose run --rm userloginclient login -u 1 -i 192.168.1.1

# Test 2: Valid IP address with different format - should succeed
Write-Host "`n[Test 2] Valid IP address (10.0.0.1)" -ForegroundColor Green
docker-compose run --rm userloginclient login -u 2 -i 10.0.0.1

# Test 3: Valid IP address with different format - should succeed
Write-Host "`n[Test 3] Valid IP address (10.0.0.1)" -ForegroundColor Green
docker-compose run --rm userloginclient login -u 2 -i 10.10.10.10

# Test 4: Invalid IP address format - should fail
Write-Host "`n[Test 4] Invalid IP address format (300.168.1.1)" -ForegroundColor Yellow
docker-compose run --rm userloginclient login -u 3 -i 300.168.1.1

# Test 5: Invalid IP address format - should fail
Write-Host "`n[Test 5] Invalid IP address format (192.168.1)" -ForegroundColor Yellow
docker-compose run --rm userloginclient login -u 4 -i 192.168.1

# Test 6: Invalid IP address format - should fail
Write-Host "`n[Test 6] Invalid IP address format (192.168.1.1.5)" -ForegroundColor Yellow
docker-compose run --rm userloginclient login -u 5 -i 192.168.1.1.5

# Test 7: Empty IP address - should fail
Write-Host "`n[Test 7] Empty IP address" -ForegroundColor Yellow
docker-compose run --rm userloginclient login -u 6 -i ''

# Test 8: Valid partial IP pattern for search - should succeed
Write-Host "`n[Test 8] Valid partial IP pattern for search (192.168)" -ForegroundColor Green
docker-compose run --rm userloginclient get-users -i 192.168

# Test 9: Invalid partial IP pattern - should fail
Write-Host "`n[Test 9] Invalid partial IP pattern (192.300)" -ForegroundColor Yellow
docker-compose run --rm userloginclient get-users -i 192.300

Write-Host "`n=== IP address validation testing complete ===" -ForegroundColor Cyan
