@echo off
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:UIMFLibrary /Version:3.8.31 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done
