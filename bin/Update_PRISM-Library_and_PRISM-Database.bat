@echo off
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:PRISM-Library       /Version:2.8.36 /S /Apply
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:PRISM-DatabaseUtils /Version:1.4.35 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done