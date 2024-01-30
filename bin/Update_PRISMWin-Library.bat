@echo off
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:PRISMWin-Library /Version:1.1.10 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done
