GlassDock v2 Enhanced
======================

This build prioritizes the uploaded "GlassDock v-2.zip" project.

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

React Bits-inspired micro interactions
---------------------------------------
Native WPF equivalents are included for:
- Shredder: animated app removal before the item is deleted
- Branched Menu: grouped Customize submenu in the dock menu
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
apps.txt is deliberately empty. Add only the apps/files/folders you want.
