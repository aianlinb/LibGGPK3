cd $(dirname $0)
rm -rf publish
dotnet publish -c Release -r win-x64 -o publish/win-x64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r win-arm64 -o publish/win-arm64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r linux-x64 -o publish/linux-x64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r linux-arm64 -o publish/linux-arm64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r osx-x64 -o publish/osx-x64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r osx-arm64 -o publish/osx-arm64 --no-self-contained --nologo -p:PublishReadyToRun=true
rm -rf publish/osx*/Eto.*
rm -rf publish/osx*/MonoMac.dll
rm -rf publish/osx*/VisualGGPK3.*
rm -rf publish/osx*/VPatchGGPK3.*
rm -rf publish/osx*/Magick.*
dotnet publish Examples/VisualGGPK3 -c Release -r osx-x64 -o publish/osx-x64/VisualGGPK3.app/Contents/MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish Examples/VisualGGPK3 -c Release -r osx-arm64 -o publish/osx-arm64/VisualGGPK3.app/Contents/MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish Examples/VPatchGGPK3 -c Release -r osx-x64 -o publish/osx-x64/VPatchGGPK3.app/Contents/MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish Examples/VPatchGGPK3 -c Release -r osx-arm64 -o publish/osx-arm64/VPatchGGPK3.app/Contents/MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
rm -rf publish/osx*/*.app/Contents/MacOS/*.app
mkdir -p publish/osx-x64/VisualGGPK3.app/Contents/Resources
mkdir -p publish/osx-x64/VPatchGGPK3.app/Contents/Resources
mkdir -p publish/osx-arm64/VisualGGPK3.app/Contents/Resources
mkdir -p publish/osx-arm64/VPatchGGPK3.app/Contents/Resources
cp -f Examples/Icon.icns publish/osx-*/*.app/Contents/Resources/Icon.icns
cp -f Examples/VisualGGPK3/Info.plist publish/osx-*/VisualGGPK3.app/Contents/Info.plist
cp -f Examples/VPatchGGPK3/Info.plist publish/osx-*/VPatchGGPK3.app/Contents/Info.plist
rm -rf publish/*/*.deps.json
rm -rf publish/win*/Xceed.Wpf.AvalonDock.*
rm -rf publish/win*/de
rm -rf publish/win*/es
rm -rf publish/win*/fr
rm -rf publish/win*/hu
rm -rf publish/win*/it
rm -rf publish/win*/pt-BR
rm -rf publish/win*/ro
rm -rf publish/win*/ru
rm -rf publish/win*/sv
rm -rf publish/win*/zh-Hans