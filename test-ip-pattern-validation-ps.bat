@echo off
echo === Testing IPv4 Pattern Validation ===
echo.

powershell -ExecutionPolicy Bypass -File .\test-ip-validation.ps1

echo === IPv4 Pattern Validation Testing Complete ===
