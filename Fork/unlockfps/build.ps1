Remove-Item -Path bin -Recurse -Force
dotnet publish -c Release -p:PublishAot=false -p:PublishSingleFile=true --self-contained true -r win-x64 -p:IncludeNativeLibrariesForSelfExtract=true
