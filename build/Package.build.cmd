@ECHO OFF
SETLOCAL
	:: SETLOCAL is on, so changes to the path not persist to the actual user's path

REM Get the version and comment from Version.txt lines 2 and 3
SET "release=1.0.0"
SET "comment="

REM If there's arguments on the command line use that as the version
IF [%1] NEQ [] (SET release=%1)
IF [%2] NEQ [] (SET comment=%2) ELSE (IF [%1] NEQ [] (SET "comment="))	

SET version=%release%
IF [%comment%] EQU [] (SET version=%release%) ELSE (SET version=%release%-%comment%)

ECHO Building version %version%

SET toolsFolder=%CD%\Tools
SET nuGetExecutable=%toolsFolder%\nuget.exe
IF NOT EXIST %nuGetExecutable% (
	ECHO Downloading https://dist.nuget.org/win-x86-commandline/latest/nuget.exe to %nuGetExecutable%
	powershell -Command "(New-Object Net.WebClient).DownloadFile('https://dist.nuget.org/win-x86-commandline/latest/nuget.exe', '%nuGetExecutable%')"
)

ECHO Restore NuGet packages
%nuGetExecutable% restore ..\src\Umbraco.Courier.Contrib.sln -Verbosity quiet

SET msbuild=%windir%\Microsoft.NET\Framework\v4.0.30319\msbuild.exe

ECHO Build the library and produce NuGet package
%msbuild% Package.build.xml /p:ProductVersion=%version%