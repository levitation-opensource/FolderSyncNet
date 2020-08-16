@echo off


title FolderSync



REM change active dir to current location
%~d0
cd /d "%~dp0"



if "%~1" neq "oneinstance" (

	if exist SingleInstanceCmd.exe (
		SingleInstanceCmd.exe "%~n0" "%~0" "oneinstance"
		goto :eof
	)
)



REM if not defined iammaximized (
REM     set iammaximized=1
REM     start "" /max /wait "%~0"
REM     exit
REM )



REM change screen dimensions
mode con: cols=200 lines=9999



:loop


FolderSync.exe


REM ping -n 2 127.0.0.1
sleep 1


goto loop

