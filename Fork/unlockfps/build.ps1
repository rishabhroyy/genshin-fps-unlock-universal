Remove-Item -Path "bin", "obj" -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish -c Release -p:PublishAot=false -p:PublishSingleFile=true --self-contained true -r win-x64 -p:IncludeNativeLibrariesForSelfExtract=true
