@echo off
rem Runs LuceneServer.NET Admin as a Windows service. Run it as administrator from the version folder:
rem   lucene-admin-service.bat install [name] [port]   create the service, or point it to this version
rem   lucene-admin-service.bat uninstall [name]        stop and delete the service
rem One root folder per instance (admin-data and _config next to the version folders).
setlocal

set COMMAND=%~1
set NAME=%~2
set PORT=%~3
if "%PORT%"=="" set PORT=5180

set SVC=lucene-admin
set DISPLAY=LuceneServer.NET Admin
if not "%NAME%"=="" (
    set SVC=lucene-admin-%NAME%
    set DISPLAY=LuceneServer.NET Admin ^(%NAME%^)
)
set ACCOUNT=NT SERVICE\%SVC%
set EVENTSOURCE=LuceneServer.NET Admin

for %%I in ("%~dp0..") do set ADMIN_ROOT=%%~fI
set APP=%~dp0admin
set EXE=%APP%\LuceneServerNET.Admin.exe

net session >nul 2>&1
if errorlevel 1 (
    echo Run this script as administrator.
    exit /b 1
)

if /i "%COMMAND%"=="install" goto install
if /i "%COMMAND%"=="uninstall" goto uninstall
echo Usage: %~nx0 install [name] [port]
echo        %~nx0 uninstall [name]
exit /b 1

:install
if not exist "%EXE%" (
    echo %EXE% not found.
    exit /b 1
)

sc query %SVC% >nul 2>&1
if errorlevel 1 goto create

rem the service exists: only update it if it belongs to this root folder
rem BINARY_PATH_NAME : "<root>\<version>\admin\LuceneServerNET.Admin.exe" --urls ...
set OLDROOT=
for /f "tokens=1,* delims=:" %%a in ('sc qc %SVC% ^| findstr /c:"BINARY_PATH_NAME"') do (
    for %%p in (%%b) do if not defined OLDROOT for %%r in ("%%~dpp..\..") do set OLDROOT=%%~fr
)
if /i not "%OLDROOT%"=="%ADMIN_ROOT%" (
    echo The service %SVC% belongs to another folder: %OLDROOT%
    echo Use another name or uninstall it first.
    exit /b 1
)
call :stop || exit /b 1
sc config %SVC% binPath= "\"%EXE%\" --urls http://localhost:%PORT%" start= delayed-auto DisplayName= "%DISPLAY%" >nul || exit /b 1
goto configure

:create
sc create %SVC% binPath= "\"%EXE%\" --urls http://localhost:%PORT%" start= delayed-auto DisplayName= "%DISPLAY%" obj= "%ACCOUNT%" >nul || exit /b 1

:configure
sc description %SVC% "LuceneServer.NET Admin UI and import jobs (%ADMIN_ROOT%)" >nul
sc failure %SVC% reset= 86400 actions= restart/60000/restart/60000/restart/60000 >nul

rem runtime state and configuration live next to the version folders, so they survive updates (as in lucene-admin.bat)
reg add "HKLM\SYSTEM\CurrentControlSet\Services\%SVC%" /v Environment /t REG_MULTI_SZ /f /s ";" /d "Admin__DataPath=%ADMIN_ROOT%\admin-data;LUCENE_ADMIN_CONFIG=%ADMIN_ROOT%\_config\admin.json" >nul || exit /b 1

for %%D in (admin-data _config) do if not exist "%ADMIN_ROOT%\%%D" mkdir "%ADMIN_ROOT%\%%D"
if not exist "%ADMIN_ROOT%\_config\admin.json" call :config

rem the service account may only write to admin-data (SQLite database, data protection keys) and read _config
icacls "%ADMIN_ROOT%\admin-data" /grant "%ACCOUNT%:(OI)(CI)M" >nul || exit /b 1
icacls "%ADMIN_ROOT%\_config" /grant "%ACCOUNT%:(OI)(CI)RX" >nul || exit /b 1

rem the event log source must exist before the service account writes to it
powershell -NoProfile -Command "if (-not [Diagnostics.EventLog]::SourceExists('%EVENTSOURCE%')) { New-EventLog -LogName Application -Source '%EVENTSOURCE%' }" >nul

sc start %SVC% >nul || exit /b 1
echo Service %SVC% runs %EXE% on http://localhost:%PORT% as %ACCOUNT%.
echo Configuration: %ADMIN_ROOT%\_config\admin.json
exit /b 0

:config
rem first install: configuration file with an empty user list
(
    echo {
    echo   "Admin": {
    echo     "Authentication": {
    echo       "Type": "Local",
    echo       "Users": [
    echo         // { "Name": "admin", "PasswordHash": "..." }  hash: admin\LuceneServerNET.Admin.exe --hash-password ^<password^>
    echo       ]
    echo     },
    echo     "Instances": [
    echo       // { "Name": "local", "Url": "http://localhost:5000", "ClientId": "", "ClientSecret": "" }
    echo     ]
    echo   }
    echo }
) > "%ADMIN_ROOT%\_config\admin.json"
echo Created %ADMIN_ROOT%\_config\admin.json: add a user, then restart the service (sc stop %SVC% ^&^& sc start %SVC%).
exit /b 0

:uninstall
sc query %SVC% >nul 2>&1
if errorlevel 1 (
    echo The service %SVC% does not exist.
    exit /b 1
)
call :stop || exit /b 1
sc delete %SVC% >nul || exit /b 1
echo Service %SVC% deleted. The folders admin-data and _config are kept.
exit /b 0

:stop
sc stop %SVC% >nul 2>&1
for /l %%i in (1,1,30) do (
    sc query %SVC% | find "STOPPED" >nul && exit /b 0
    timeout /t 1 /nobreak >nul
)
echo The service %SVC% did not stop.
exit /b 1
