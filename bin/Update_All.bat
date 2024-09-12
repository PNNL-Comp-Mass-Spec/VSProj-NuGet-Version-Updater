@echo on
call Update_PRISM-Library_and_PRISM-Database.bat NoPause

@echo on
call Update_PRISMWin-Library.bat NoPause

@echo on
call Update_MyEMSLReader.bat NoPause

@echo on
call Update_ProteinFileReader.bat NoPause

@echo on
call Update_PSI_Interface.bat NoBryson

@echo on
call Update_ThermoRawFileReader.bat NoPause

@echo on
call Update_UIMF-Library.bat NoPause

@echo on
call Update_Nerdbank_Git_Versioning.bat NoBryson

pause
