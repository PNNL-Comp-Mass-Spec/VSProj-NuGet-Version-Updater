@echo off
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:PRISM-Library       /Version:2.9.25 /S /Apply
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:PRISM-DatabaseUtils /Version:1.5.12 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done