rm -rf publish
dotnet publish -c Release -r win-x64 -o publish/win-x64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r win-arm64 -o publish/win-arm64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r linux-x64 -o publish/linux-x64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r linux-arm64 -o publish/linux-arm64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r osx-x64 -o publish/osx-x64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r osx-arm64 -o publish/osx-arm64 --no-self-contained --nologo -p:PublishReadyToRun=true
(echo "chmod -R +x *" && echo "xattr -c -r .") > publish/osx-x64/FirstRun.sh
(echo "chmod -R +x *" && echo "xattr -c -r .") > publish/osx-arm64/FirstRun.sh