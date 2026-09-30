@echo off
"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\cl.exe" /nologo /W4 /WX /TC /O2 /GS- "%~dp0PolicyTests.c" /Fo"%~dp0..\..\artifacts\driver\ZeroHoldPolicyTests.obj" /Fe"%~dp0..\..\artifacts\driver\ZeroHoldPolicyTests.exe" /link /NODEFAULTLIB /SUBSYSTEM:CONSOLE /ENTRY:mainCRTStartup
if errorlevel 1 exit /b 1
"%~dp0..\..\artifacts\driver\ZeroHoldPolicyTests.exe"
if errorlevel 1 exit /b %errorlevel%
echo PASS: 46 native zero hold / bounded zero / power / temperature / lease checks.
