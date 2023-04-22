@echo off
echo Updating projects below F:\Documents\Projects\DataMining
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects\DataMining" /Package:PSI_Interface /Version:2.5.92 /S /Apply

echo Press any key to also update projects below F:\Documents\Projects\Bryson_Gibbons\
pause

VSProjNuGetVersionUpdater.exe "F:\Documents\Projects\Bryson_Gibbons\" /Package:PSI_Interface /Version:2.5.92 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done