echo on
set env=DEV
IF NOT DEFINED UpdateSetup set UpdateSetup=false

rem set TargetBin=C:\temp\New folder
set TargetBin=D:\dev.darioa\97. diginsight\smartcache\Common.SmartCache

IF %OutDir% == bin\Debug\net472\ (
	IF /I %UpdateSetup%==True echo  robocopy .\%OutDir% "%TargetBin%" *.dll
	IF /I %UpdateSetup%==True robocopy .\%OutDir% "%TargetBin%" Common.Diagnostics*.dll
    IF %ERRORLEVEL% GTR 7 goto fail
	IF /I %UpdateSetup%==True echo  robocopy .\%OutDir% "%TargetBin%" *.pdb
	IF /I %UpdateSetup%==True robocopy .\%OutDir% "%TargetBin%" Common.Diagnostics*.pdb
    IF %ERRORLEVEL% GTR 7 goto fail
)

exit /b 0
rem :errorHandling
:fail  
exit /b 1

 