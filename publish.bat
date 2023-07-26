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
del /f /s /q publish\osx-x64\Eto.*
del /f /s /q publish\osx-arm64\Eto.*
del /f /s /q publish\osx-x64\MonoMac.dll
del /f /s /q publish\osx-arm64\MonoMac.dll
del /f /s /q publish\osx-x64\VisualGGPK3*
del /f /s /q publish\osx-arm64\VisualGGPK3*
del /f /s /q publish\osx-x64\VPatchGGPK3*
del /f /s /q publish\osx-arm64\VPatchGGPK3*
dotnet publish Examples\VisualGGPK3 -c Release -r osx-x64 -o publish\osx-x64\VisualGGPK3.app\Contents\MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish Examples\VisualGGPK3 -c Release -r osx-arm64 -o publish\osx-arm64\VisualGGPK3.app\Contents\MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish Examples\VPatchGGPK3 -c Release -r osx-x64 -o publish\osx-x64\VPatchGGPK3.app\Contents\MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
dotnet publish Examples\VPatchGGPK3 -c Release -r osx-arm64 -o publish\osx-arm64\VPatchGGPK3.app\Contents\MacOS --no-self-contained --nologo -p:PublishReadyToRun=true
rmdir /s /q publish\osx-x64\VisualGGPK3.app\Contents\MacOS\VisualGGPK3.app
rmdir /s /q publish\osx-x64\VPatchGGPK3.app\Contents\MacOS\VPatchGGPK3.app
rmdir /s /q publish\osx-arm64\VisualGGPK3.app\Contents\MacOS\VisualGGPK3.app
rmdir /s /q publish\osx-arm64\VPatchGGPK3.app\Contents\MacOS\VPatchGGPK3.app
mkdir publish\osx-x64\VisualGGPK3.app\Contents\Resources
mkdir publish\osx-x64\VPatchGGPK3.app\Contents\Resources
copy /y Examples\Icon.icns publish\osx-x64\VisualGGPK3.app\Contents\Resources\Icon.icns
copy /y Examples\Icon.icns publish\osx-x64\VPatchGGPK3.app\Contents\Resources\Icon.icns
copy /y Examples\Icon.icns publish\osx-arm64\VisualGGPK3.app\Contents\Resources\Icon.icns
copy /y Examples\Icon.icns publish\osx-arm64\VPatchGGPK3.app\Contents\Resources\Icon.icns
copy /y Examples\VisualGGPK3\Info.plist publish\osx-x64\VisualGGPK3.app\Contents\Info.plist
copy /y Examples\VPatchGGPK3\Info.plist publish\osx-x64\VPatchGGPK3.app\Contents\Info.plist
copy /y Examples\VisualGGPK3\Info.plist publish\osx-arm64\VisualGGPK3.app\Contents\Info.plist
copy /y Examples\VPatchGGPK3\Info.plist publish\osx-arm64\VPatchGGPK3.app\Contents\Info.plist
(echo chmod -R +x .&& echo xattr -c -r .) > publish\osx-x64\FirstRun.sh
(echo chmod -R +x .&& echo xattr -c -r .) > publish\osx-arm64\FirstRun.sh
del /f /s /q publish\win-x64\*.deps.json
del /f /s /q publish\win-arm64\*.deps.json
del /f /s /q publish\linux-x64\*.deps.json
del /f /s /q publish\linux-arm64\*.deps.json
del /f /s /q publish\osx-x64\*.deps.json
del /f /s /q publish\osx-arm64\*.deps.json