chdir /d %~dp0
rmdir /s /q publish
dotnet publish -c Release -r win-x64 -o publish\win-x64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r win-arm64 -o publish\win-arm64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r linux-x64 -o publish\linux-x64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r linux-arm64 -o publish\linux-arm64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r osx-x64 -o publish\osx-x64 --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish -c Release -r osx-arm64 -o publish\osx-arm64 --no-self-contained --nologo -p:PublishReadyToRun=true
rmdir /s /q publish\osx-x64\VisualGGPK3.app
rmdir /s /q publish\osx-x64\VPatchGGPK3.app
rmdir /s /q publish\osx-arm64\VisualGGPK3.app
rmdir /s /q publish\osx-arm64\VPatchGGPK3.app
del /q publish\osx-x64\Eto.*
del /q publish\osx-arm64\Eto.*
del /q publish\osx-x64\MonoMac.dll
del /q publish\osx-arm64\MonoMac.dll
del /s /q publish\osx-x64\VisualGGPK3*
del /s /q publish\osx-arm64\VisualGGPK3*
del /s /q publish\osx-x64\VPatchGGPK3*
del /s /q publish\osx-arm64\VPatchGGPK3*
del /q publish\osx-x64\Magick.*
del /q publish\osx-arm64\Magick.*
dotnet publish Examples\VisualGGPK3 -c Release -r osx-x64 -o publish\osx-x64\VisualGGPK3.app\Contents\MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish Examples\VisualGGPK3 -c Release -r osx-arm64 -o publish\osx-arm64\VisualGGPK3.app\Contents\MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish Examples\VPatchGGPK3 -c Release -r osx-x64 -o publish\osx-x64\VPatchGGPK3.app\Contents\MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish Examples\VPatchGGPK3 -c Release -r osx-arm64 -o publish\osx-arm64\VPatchGGPK3.app\Contents\MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
rmdir /s /q publish\osx-x64\VisualGGPK3.app\Contents\MacOS\VisualGGPK3.app
rmdir /s /q publish\osx-x64\VPatchGGPK3.app\Contents\MacOS\VPatchGGPK3.app
rmdir /s /q publish\osx-arm64\VisualGGPK3.app\Contents\MacOS\VisualGGPK3.app
rmdir /s /q publish\osx-arm64\VPatchGGPK3.app\Contents\MacOS\VPatchGGPK3.app
mkdir publish\osx-x64\VPatchGGPK3.app\Contents\Resources
mkdir publish\osx-x64\VisualGGPK3.app\Contents\Resources
mkdir publish\osx-arm64\VPatchGGPK3.app\Contents\Resources
mkdir publish\osx-arm64\VisualGGPK3.app\Contents\Resources
copy /y Examples\Icon.icns publish\osx-x64\VisualGGPK3.app\Contents\Resources\Icon.icns
copy /y Examples\Icon.icns publish\osx-x64\VPatchGGPK3.app\Contents\Resources\Icon.icns
copy /y Examples\Icon.icns publish\osx-arm64\VisualGGPK3.app\Contents\Resources\Icon.icns
copy /y Examples\Icon.icns publish\osx-arm64\VPatchGGPK3.app\Contents\Resources\Icon.icns
copy /y Examples\VisualGGPK3\Info.plist publish\osx-x64\VisualGGPK3.app\Contents\Info.plist
copy /y Examples\VPatchGGPK3\Info.plist publish\osx-x64\VPatchGGPK3.app\Contents\Info.plist
copy /y Examples\VisualGGPK3\Info.plist publish\osx-arm64\VisualGGPK3.app\Contents\Info.plist
copy /y Examples\VPatchGGPK3\Info.plist publish\osx-arm64\VPatchGGPK3.app\Contents\Info.plist
del /q publish\win-x64\*.deps.json
del /q publish\win-arm64\*.deps.json
del /q publish\linux-x64\*.deps.json
del /q publish\linux-arm64\*.deps.json
del /q publish\osx-x64\*.deps.json
del /q publish\osx-arm64\*.deps.json
del /q publish\win-x64\Xceed.Wpf.AvalonDock.*
rmdir /s /q publish\win-x64\de
rmdir /s /q publish\win-x64\es
rmdir /s /q publish\win-x64\fr
rmdir /s /q publish\win-x64\hu
rmdir /s /q publish\win-x64\it
rmdir /s /q publish\win-x64\pt-BR
rmdir /s /q publish\win-x64\ro
rmdir /s /q publish\win-x64\ru
rmdir /s /q publish\win-x64\sv
rmdir /s /q publish\win-x64\zh-Hans
del /q publish\win-arm64\Xceed.Wpf.AvalonDock.*
rmdir /s /q publish\win-arm64\de
rmdir /s /q publish\win-arm64\es
rmdir /s /q publish\win-arm64\fr
rmdir /s /q publish\win-arm64\hu
rmdir /s /q publish\win-arm64\it
rmdir /s /q publish\win-arm64\pt-BR
rmdir /s /q publish\win-arm64\ro
rmdir /s /q publish\win-arm64\ru
rmdir /s /q publish\win-arm64\sv
rmdir /s /q publish\win-arm64\zh-Hans