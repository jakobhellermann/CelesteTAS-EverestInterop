publish:
    dotnet publish --no-restore -c Release CelesteTas-EverestInterop
    dotnet publish --no-restore -c Release Studio/CelesteStudio.WPF
    tar.exe -a -c -f out/CelesteStudio.zip Studio/CelesteStudio.WPF/bin/Release/net7.0-windows/win-x64/publish/*
    cp thunderstore/build/dll/* thunderstore/build/*.zip out
    
