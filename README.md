[![NuGet](https://img.shields.io/nuget/v/LibGGPK3.LibBundledGGPK3)](https://www.nuget.org/packages?q=LibGGPK3)

Library for Content.ggpk of game: Path of Exile.  
Rewrite of https://github.com/aianlinb/LibGGPK2

Expected to work on Windows, Linux and macOS

## Notice
- All projects in this repository are not thread-safe.  
Do not process a single ggpk file with more than one thread.
- Each update does not necessarily guarantee forward compatibility.  
Please read the commit messages carefully and check whether your project is working properly before updating to a new version.

# LibGGPK3
Handle the Content.ggpk

# LibBundle3
Handle the *.bundle.bin files under Bundles2 folder  
For Steam/Epic users

# LibBundledGGPK3
Combination of LibGGPK3 and LibBundle3  
Handle both Content.ggpk and the bundle files in it  
For Standalone-Client users

# Examples
Sample programs to realize some simple features of the libraries  
***VisualGGPK3 is not yet complete***