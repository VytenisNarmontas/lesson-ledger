$ErrorActionPreference = 'Stop'
Start-Process -FilePath (Join-Path $PSScriptRoot 'LessonLedger.exe') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
