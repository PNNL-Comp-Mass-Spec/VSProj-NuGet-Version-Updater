@echo off

rem Copy new versions of the ThermoRawFileReader
rem from \\proto-2\CI_Publish\NuGet\
rem to   C:\NuPkg

VSProjNuGetVersionUpdater.exe "F:\Documents\Projects" /Package:ThermoRawFileReader /Version:4.2.48 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done
