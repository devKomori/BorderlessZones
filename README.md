# BorderlessZones

BorderlessZones is a Windows application that removes window borders and title bars, allowing you to position and resize applications

> Created to get the most of my ultrawide monitor, allowing me to play games in a 27 inch sized window without borders or title bars, while leaving room for things like Discord and Spotify.
>
> It is particularly useful when combined with Microsoft's FancyZones, which lets you quickly snap other applications into the areas of your screen you setup.

## Releases
You can download the latest ```.exe or ZIP``` in [Releases](https://github.com/devKomori/BorderlessZones/releases)

## Features

- **Borderless Windows** – Applies windowed mode but removes title bars and borders
  
- **Custom Zones** – Configure window position (X/Y offsets) and Width/Height
  
- **Multi-Monitor Support** – Select any connected display
  
- **Per-Application State** – Apply borderless mode to multiple applications independently. Switch between applications without losing their applied states, and return to any previously modified application to restore it.
  
- **Restore Windows** – Restore the original window state: size, position, and borders.
  
- **Cover Taskbar** – If you have auto hide taskbar turned off, this makes the selected application the topmost window and will cover the taskbar.

## Usage
**Before Borderless**
<img width="3148" height="1438" alt="before" src="https://github.com/user-attachments/assets/854373e9-30bc-44d5-b71d-ffcf31846e33" />

**After Borderless**
<img width="3125" height="1438" alt="after" src="https://github.com/user-attachments/assets/22a90e16-5ce9-470a-80e3-76213c99965e" />
---
1. Select a running application from the dropdown.
2. Choose your target monitor.
3. Configure the window's position and dimensions.
5. Click **Enable Borderless** to apply.
6. Click **Restore** to revert the window to its original state.

> Coordinates are relative to the selected monitor, with (0, 0) representing its top left corner.
>
> For example, on my **5120 × 1440 ultrawide monitor**, a game configured with a width of **2560** and height of **1440** can occupy either half of the screen:
>
> - **Left Half:** Setting X Offset to `0` and Y Offset to `0` positions the game at the top-left corner of the monitor.
>   
> - **Right Half:** Setting X Offset to `2560` and Y Offset to `0` shifts the game to the right, positioning its top-left corner halfway across the monitor (5120 ÷ 2 = 2560).
>
> This allows me to play games in a borderless 2560 × 1440 window while keeping the other half of my ultrawide available for multitasking.

## Build from source
Requires Windows and the **.NET 10 SDK**.
1. Download as a ZIP and extract.
2. Open a terminal in the project directory and run 
```bash
dotnet build -c Release
```
The compiled executable will be located in bin/Release/net10.0-windows/
---
