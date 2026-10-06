@echo off
rem Build LineArtTool.exe (pure ASCII file, run from this folder or with full paths)
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /nologo /target:winexe /optimize+ /codepage:65001 /win32manifest:app.manifest /win32icon:app.ico /out:LineArtTool.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll LineArtTool.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK
