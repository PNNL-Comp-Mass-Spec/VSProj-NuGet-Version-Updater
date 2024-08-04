@echo off
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:ProteinFileReader /Version:3.1.0 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done