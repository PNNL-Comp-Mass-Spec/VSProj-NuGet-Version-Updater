@echo off
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:MyEMSL-Reader       /Version:2.1.113 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done