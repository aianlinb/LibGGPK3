rmdir /s /q publish
dotnet publish -c Windows -r win-x64 -o publish/win-x64 --no-self-contained -p:PublishReadyToRun=true --nologo
dotnet publish -c Linux -r linux-x64 -o publish/linux-x64 --no-self-contained -p:PublishReadyToRun=true --nologo
dotnet publish -c MacOS -r osx-x64 -o publish/osx-x64 --no-self-contained -p:PublishReadyToRun=true --nologo
(echo chmod +x *&& echo xattr -c .) > publish/osx-x64/FirstRun.sh
rmdir /s /q "publish/osx-x64/VisualGGPK3.app"