@echo off
echo === Testing IPv4 Pattern Validation ===
echo.

echo [Test 1] Populate database with test data
docker-compose run --rm userloginclient login -u 1 -i 192.168.1.1
docker-compose run --rm userloginclient login -u 2 -i 192.168.2.5
docker-compose run --rm userloginclient login -u 3 -i 192.168.10.100
docker-compose run --rm userloginclient login -u 4 -i 10.0.0.1
docker-compose run --rm userloginclient login -u 5 -i 10.10.10.10
echo.

echo [Test 2] Valid full IP address pattern (192.168.1.1)
docker-compose run --rm userloginclient get-users -i 192.168.1.1
echo.

echo [Test 3] Valid partial IP pattern with three octets (192.168.1)
docker-compose run --rm userloginclient get-users -i 192.168.1
echo.

echo [Test 4] Valid partial IP pattern with two octets (192.168)
docker-compose run --rm userloginclient get-users -i 192.168
echo.

echo [Test 5] Valid partial IP pattern with trailing dot (192.168.)
docker-compose run --rm userloginclient get-users -i 192.168.
echo.

echo [Test 6] Valid partial IP pattern - single octet (10)
docker-compose run --rm userloginclient get-users -i 10
echo.

echo [Test 7] Invalid partial IP pattern (octet too large) (300.168)
docker-compose run --rm userloginclient get-users -i 300.168
echo.

echo [Test 8] Invalid partial IP pattern (negative number) (-10.10)
docker-compose run --rm userloginclient get-users -i -10.10
echo.

echo [Test 9] Invalid partial IP pattern (non-numeric) (abc.def)
docker-compose run --rm userloginclient get-users -i abc.def
echo.

echo [Test 10] Empty pattern
docker-compose run --rm userloginclient get-users -i ""
echo.

echo === IPv4 Pattern Validation Testing Complete ===
