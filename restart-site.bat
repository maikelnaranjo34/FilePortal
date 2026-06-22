@echo off
echo Fixing and restarting FilePortal IIS site...
echo.

REM Fix app pool CLR version to "No Managed Code" (required for ASP.NET Core)
"%SystemRoot%\system32\inetsrv\appcmd" set apppool /apppool.name:"FilePortalPool" /managedRuntimeVersion:""
echo.

REM Stop and restart the pool to apply changes
"%SystemRoot%\system32\inetsrv\appcmd" stop apppool /apppool.name:"FilePortalPool"
timeout /t 2 /nobreak >nul
"%SystemRoot%\system32\inetsrv\appcmd" start apppool /apppool.name:"FilePortalPool"
echo.

REM Show final state
echo Current app pool state:
"%SystemRoot%\system32\inetsrv\appcmd" list apppool "FilePortalPool"
echo.
echo Done. Check the browser now.
pause
