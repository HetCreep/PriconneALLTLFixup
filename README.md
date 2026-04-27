# PriconneALLTLFixup

Fixes a couple issues when using XUAT with Priconne, as well as adding QoL features.

## Features
- **Removes the typewriter effect** from all dialog, improving the speed and accuracy of the translation.
- Enables you to **search units by their English name** in the unit list.
- **Adds subtitles to all movies**, even if you downloaded them with audio. Usually they only show up if you download without voices.
- **Translates subtitles and story before they are displayed**, so you don't have to wait for the translation to happen.
- **Adds number separators** to most large numbers in the game, removing the need to count endless zeroes.
- Allows you to **resize the game window to any size**, instead of being limited to 720p. Also enables the maximize button.
- Automatically **closes the Sugoi translation backend terminal window** when you close the game, should you use that.

## Fixes
- Fixes the monster detail window becoming unreadable after translation.
- Fixes skill bubbles not disappearing after translation.
- Fixes issues with story dialog not translating or even disappearing after the character name gets translated.
- Fixes text overflowing various dialog boxes and speech bubbles.
- Fixes various issues with font sizes.

## Contributing

To set up the development environment, create a file called `PriconneALLTLFixup.csproj.user` with the following content, and enter the path where your game is installed:

```
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <!-- Change this path-->
    <GameDir>C:\Users\YourUsername\priconner</GameDir>
  </PropertyGroup>
</Project>
```
