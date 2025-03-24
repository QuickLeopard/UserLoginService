@echo off
echo === Testing IP address validation ===
echo.

echo [Test 1] Valid IPv4 address (192.168.1.1)
docker-compose run --rm userloginclient login -u 1 -i 192.168.1.1
echo.

echo [Test 2] Valid IPv4 address (10.0.0.1)
docker-compose run --rm userloginclient login -u 2 -i 10.0.0.1
echo.

echo [Test 3] Invalid IPv4 address format (300.168.1.1)
docker-compose run --rm userloginclient login -u 3 -i 300.168.1.1
echo.

echo [Test 4] Invalid IPv4 address format (192.168.1)
docker-compose run --rm userloginclient login -u 4 -i 192.168.1
echo.

echo [Test 5] Invalid IPv4 address format (192.168.1.1.5)
docker-compose run --rm userloginclient login -u 5 -i 192.168.1.1.5
echo.

echo [Test 6] Empty IP address
docker-compose run --rm userloginclient login -u 6 -i ""
echo.

echo === IPv6 Address Testing ===
echo.

echo [Test 7] Valid IPv6 address (2001:0db8:85a3:0000:0000:8a2e:0370:7334)
docker-compose run --rm userloginclient login -u 7 -i 2001:0db8:85a3:0000:0000:8a2e:0370:7334
echo.

echo [Test 8] Valid IPv6 address - compressed format (2001:db8:85a3::8a2e:370:7334)
docker-compose run --rm userloginclient login -u 8 -i 2001:db8:85a3::8a2e:370:7334
echo.

echo [Test 9] Valid IPv6 address - localhost (::1)
docker-compose run --rm userloginclient login -u 9 -i ::1
echo.

echo [Test 10] Valid IPv6 address - IPv4-mapped (::ffff:192.168.1.1)
docker-compose run --rm userloginclient login -u 10 -i ::ffff:192.168.1.1
echo.

echo [Test 11] Invalid IPv6 address (2001:0db8:85a3:0000:0000:8a2e:0370:733Z)
docker-compose run --rm userloginclient login -u 11 -i 2001:0db8:85a3:0000:0000:8a2e:0370:733Z
echo.

echo [Test 12] Invalid IPv6 address (too many segments) (2001:0db8:85a3:0000:0000:8a2e:0370:7334:5678)
docker-compose run --rm userloginclient login -u 12 -i 2001:0db8:85a3:0000:0000:8a2e:0370:7334:5678
echo.

echo === IP Pattern Search Testing ===
echo.

echo [Test 13] Valid IPv4 pattern search (192.168.1)
docker-compose run --rm userloginclient get-users -i 192.168.1
echo.

echo [Test 14] Valid IPv6 pattern search (2001:db8)
docker-compose run --rm userloginclient get-users -i 2001:db8
echo.

echo [Test 15] Invalid pattern search
docker-compose run --rm userloginclient get-users -i invalid-pattern
echo.

echo [Test 16] Check available commands
docker-compose run --rm userloginclient --help
echo.

echo === IP address validation testing complete ===
