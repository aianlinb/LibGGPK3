[![NuGet](https://img.shields.io/nuget/v/LibGGPK3.LibBundledGGPK3)](https://www.nuget.org/packages?q=LibGGPK3)

## Overview
A cross-platfrom library for working with Content.ggpk from the game Path of Exile. 
Rewrite of: https://github.com/aianlinb/LibGGPK2

## Notice
- Unauthorized modification, redistribution, or commercial use without open-source is prohibited. Please contact the author if needed.  
- Projects in this repository may not be fully thread-safe.  
Exercise caution when processing a single ggpk file from multiple threads.  
- Updates do not guarantee forward compatibility.  
Please review the commit history carefully and verify that your project continues to function as expected before upgrading to a new version.

# LibGGPK3
Handles the Content.ggpk

# LibBundle3
Handles the *.bundle.bin files in the Bundles2 directory  
For Steam/Epic users

# LibBundledGGPK3
Combination of LibGGPK3 and LibBundle3  
Handle both Content.ggpk and the bundle files in it  
For Standalone-Client users

# Examples
Sample programs to realize some simple features of the libraries  
***VisualGGPK3 is not yet completed***