$ErrorActionPreference = 'Stop'
$lessonRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework compiler is missing.' }
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu "/out:$lessonRoot\LessonLedger.exe" "/win32manifest:$lessonRoot\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Core.dll /reference:System.Security.dll /reference:Microsoft.CSharp.dll "$lessonRoot\LessonLedger.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'Built LessonLedger.exe'
