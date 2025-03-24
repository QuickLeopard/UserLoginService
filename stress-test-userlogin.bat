@echo off
echo === STRESS TESTING USER LOGIN SERVICE ===
echo This script will perform various stress tests on the UserLogin functionality
echo with both IPv4 and IPv6 addresses to verify system stability under load
echo.

echo === Test Configuration ===
set BASE_USERS=100
set BASE_IPS=20
set BASE_REQUESTS=1000
set BASE_PARALLEL=10
set BASE_DELAY=5

echo Basic test parameters:
echo - Users: %BASE_USERS%
echo - IP addresses: %BASE_IPS%
echo - Requests: %BASE_REQUESTS%
echo - Parallel connections: %BASE_PARALLEL%
echo - Delay between requests (ms): %BASE_DELAY%
echo.

echo === Phase 1: Mixed IPv4/IPv6 Addresses Load Test ===
echo Running load test with mixed IPv4 and IPv6 addresses...
docker-compose run --rm userloginclient load-test -u %BASE_USERS% -i %BASE_IPS% -c %BASE_REQUESTS% -p %BASE_PARALLEL% -d %BASE_DELAY% -l
echo.

echo === Phase 2: High Concurrency Test ===
echo Running high concurrency test with 50 parallel connections...
docker-compose run --rm userloginclient load-test -u %BASE_USERS% -i %BASE_IPS% -c %BASE_REQUESTS% -p 50 -d %BASE_DELAY% -l
echo.

echo === Phase 3: Burst Test (Zero Delay) ===
echo Running burst test with no delay between requests...
docker-compose run --rm userloginclient load-test -u %BASE_USERS% -i %BASE_IPS% -c %BASE_REQUESTS% -p %BASE_PARALLEL% -d 0 -l
echo.

echo === Phase 4: High Volume Test ===
echo Running high volume test with 5,000 login requests...
docker-compose run --rm userloginclient load-test -u %BASE_USERS% -i %BASE_IPS% -c 5000 -p %BASE_PARALLEL% -d %BASE_DELAY% -l
echo.

echo === Phase 5: IP Pattern Search Test ===
echo Testing IP pattern search performance after load testing...
echo.
echo IPv4 pattern search:
docker-compose run --rm userloginclient get-users -i 192.168.1
echo.
echo IPv6 pattern search:
docker-compose run --rm userloginclient get-users -i 2001:db8
echo.

echo === Phase 6: User IP History Query Test ===
echo Testing user IP history query performance for sample users...
echo.
for /l %%i in (1, 25, %BASE_USERS%) do (
    echo User ID: %%i
    docker-compose run --rm userloginclient get-ips -u %%i
    echo.
)

echo === Stress Test Complete ===
echo Results summary:
echo - Check the test metrics in the output above
echo - Verify all services remained stable under load
echo - Check logs for any errors or performance bottlenecks
echo.

echo === System Status ===
docker-compose ps
