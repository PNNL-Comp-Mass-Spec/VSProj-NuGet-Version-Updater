@echo off
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:ProteinFileReader /Version:3.2.3 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done