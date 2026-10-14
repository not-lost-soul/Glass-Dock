GlassDock v2
======================
Design direction
----------------
The material is intentionally not just a dark translucent rectangle. It uses:
- live desktop sampling while the dock is open
- blurred environment + sharper edge information
- lens-like edge refraction
- adaptive tinting and contrast
- cursor-responsive specular light
- multi-layer highlights and depth shadows
- spring-based width/height morphing
- liquid bulge and motion smear
- icon magnification
- album-art-driven accenting
- custom app list with NO default apps

React Bits-inspired micro-interactions.
---------------------------------------
Native WPF equivalents are included for:
- Shredder: animated app removal before the item is deleted
- Branched Menu: groupedCustomisee submenu in the dock menu
- Jelly Radio: springy motion-character selector
- Glide Select: animated segmented selectors
- Slosh Gauge: native liquid/inertia gauge component available to the UI layer
- Spring Check: spring-animated settings toggles

Important
---------
Apple's Liquid Glass is a native Apple platform material. Windows/WPF cannot directly call Apple's private/native Liquid Glass implementation.
This project instead recreates the documented optical and motion principles: lensing/refraction,
translucency, specular highlights, adaptive contrast, depth and fluid morphing.

Build
-----
Run build.bat on Windows 10/11 with the .NET Framework 4.x C# compiler available.
The source is GlassDock.cs.

First run
---------
apps.txt is deliberately empty. Add only the apps/files/folders you want.**GlassDock v2 Enhanced**

This build focuses on the "GlassDock v-2.zip" project.

**Design Direction**
--------------------------------
The design avoids being merely a dark translucent rectangle. Instead, it features:
- Live desktop sampling while the dock is open
- Blurred backgrounds with sharper edge details
- Lens-like edge refraction effects
- Adaptive tinting and contrast adjustments
- Cursor-responsive specular lighting
- Multi-layer highlights and depth shadows
- Spring-based morphing for width and height
- Liquid bulge effects and motion smear
- Icon magnification
- Album art-driven accenting
- A customizable app list with no default applications

**Micro-Interactions Inspired by React Bits**
---------------------------------------
Native WPF equivalents are included for the following features:
- **Shredder:** Animated app removal that occurs before the item is deleted
- **Branched Menu:** A customizable submenu for grouped items in the dock menu
- **Jelly Radio:** A springy motion character selector
- **Glide Select:** Animated segmented selectors
- **Slosh Gauge:** A native liquid/inertia gauge component available to the UI layer
- **Spring Check:** Spring-animated toggles for settings

**Important Note**
--------------------------------
Apple's Liquid Glass is a native material exclusive to Apple platforms, and Windows/WPF cannot directly access its private/native implementation. This project instead recreates the documented optical and motion principles, such as lensing/refraction, translucency, specular highlights, adaptive contrast, depth effects, and fluid morphing.

**Build Instructions**
--------------------------------
To build the project, run `build.bat` on Windows 10/11 with the .NET Framework 4.x C# compiler available. The source file is named `GlassDock.cs`.

**First Run**
--------------------------------
The `apps.txt` file is intentionally empty. Please add only the apps, files, or folders that you wish to include.
