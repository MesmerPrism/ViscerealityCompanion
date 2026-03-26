Option Explicit

Dim shell
Dim fso
Dim scriptPath
Dim command
Dim i

Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

scriptPath = fso.BuildPath(fso.GetParentFolderName(WScript.ScriptFullName), "Start-Desktop-App.ps1")
command = "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File " & Quote(scriptPath)

For i = 0 To WScript.Arguments.Count - 1
    command = command & " " & Quote(WScript.Arguments.Item(i))
Next

shell.Run command, 0, False

Function Quote(value)
    Quote = Chr(34) & value & Chr(34)
End Function
