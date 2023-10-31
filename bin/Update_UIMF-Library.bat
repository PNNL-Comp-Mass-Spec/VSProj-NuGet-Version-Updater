@echo off
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:UIMFLibrary /Version:3.8.26 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done
