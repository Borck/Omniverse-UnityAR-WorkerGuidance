$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\..\.."
$project = Join-Path $root "tools\proto-csharp\ProtoCSharpGen.csproj"

dotnet build $project -nologo -v minimal
