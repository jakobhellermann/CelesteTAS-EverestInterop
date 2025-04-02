set windows-shell := ["powershell.exe", "-NoLogo", "-Command"]

[linux]
publish:
    dotnet publish --no-restore -c Release CelesteTAS-EverestInterop
    dotnet publish --no-restore -c Release Studio/CelesteStudio.WPF
    
    New-Item -ItemType Directory -Path out -Force
    Compress-Archive -Path Studio/CelesteStudio.WPF/bin/Release/net7.0-windows/win-x64/publish/* -DestinationPath out/CelesteStudio.zip -Force
    
    cp thunderstore/build/dll/* out
    cp thunderstore/build/*.zip out
    
[windows]
publish:
    dotnet publish --no-restore -c Release CelesteTAS-EverestInterop
    dotnet publish --no-restore -c Release Studio/CelesteStudio.WPF
    
    New-Item -ItemType Directory -Path out -Force
    Compress-Archive -Path Studio/CelesteStudio.WPF/bin/Release/net7.0-windows/win-x64/publish/* -DestinationPath out/CelesteStudio.zip -Force
    
    cp thunderstore/build/dll/* out
    cp thunderstore/build/*.zip out
    

[windows]
clean:
    if (Test-Path "Studio/CelesteStudio.WPF/bin") { Remove-Item "Studio/CelesteStudio.WPF/bin" -Recurse -Force }