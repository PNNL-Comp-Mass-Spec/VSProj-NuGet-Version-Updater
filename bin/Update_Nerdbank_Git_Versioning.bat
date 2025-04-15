@echo off
echo Updating projects below F:\Documents\Projects\DataMining
VSProjNuGetVersionUpdater.exe "F:\Documents\Projects\DataMining" /Package:Nerdbank.GitVersioning /Version:3.7.115 /S /Apply

IF [%1] == [NoBryson] GOTO Done

echo Press any key to also update projects below F:\Documents\Projects\Bryson_Gibbons\
pause

VSProjNuGetVersionUpdater.exe "F:\Documents\Projects\Bryson_Gibbons\" /Package:Nerdbank.GitVersioning /Version:3.7.115 /S /Apply

IF [%1] == [NoPause] GOTO Done

pause

:Done