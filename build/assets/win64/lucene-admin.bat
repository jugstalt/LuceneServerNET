@echo off
rem Starts LuceneServer.NET Admin in this console (as a service: lucene-admin-service.bat).
rem runtime state and configuration live next to the version folders, so they survive updates
for %%I in ("%~dp0..") do set ADMIN_ROOT=%%~fI
set Admin__DataPath=%ADMIN_ROOT%\admin-data
set LUCENE_ADMIN_CONFIG=%ADMIN_ROOT%\_config\admin.json
if "%ASPNETCORE_URLS%"=="" set ASPNETCORE_URLS=http://localhost:5180

cd /d "%~dp0admin"
dotnet LuceneServerNET.Admin.dll %*
