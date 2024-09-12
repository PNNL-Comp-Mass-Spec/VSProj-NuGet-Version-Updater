@echo off
echo Updating projects below F:\Documents\Projects\DataMining
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects\DataMining" /Package:PSI_Interface /Version:2.6.4 /S /Apply

IF [%1] == [NoBryson] GOTO Done

echo Press any key to also update projects below F:\Documents\Projects\Bryson_Gibbons\
pause

VSProjNuGetVersionUpdater.exe "F:\Documents\Projects\Bryson_Gibbons\" /Package:PSI_Interface /Version:2.6.4 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done