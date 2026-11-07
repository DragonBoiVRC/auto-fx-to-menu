# Auto FX To Menu

Builds your VRChat **Expression Parameters** and **Expressions Menus** from your FX controller.
Name your FX float parameters `Category/Item` (`Tops/Hoodie`, `Bottoms/Jeans`, `Texture Swaps/Coat`)
and the tool turns each category into a submenu, so you don't hand-build them.

Plain VRChat Avatars SDK. No Modular Avatar or VRCFury needed.

## Use

1. **Tools > Auto FX To Menu**.
2. Drag in your **FX controller**, your **Main Menu** asset and your **Parameters** asset.
3. **Scan FX Layer**. Every float named `Category/Item` is listed. Fix the category or type if the scan guessed wrong, or untick what you don't want.
4. **Generate Menus + Parameters**.

Menus are written to a `Generated/` folder next to your Main Menu asset. Each category becomes a
submenu on the Main Menu, replacing any submenu of the same name. Back up your Main Menu and
Parameters assets first, since they are edited in place.

## Types

| Type | Menu control | Notes |
|------|--------------|-------|
| **Bool** | Toggle | The default. |
| **Int** | Submenu of buttons | Values run 1..N. The scan suggests Int when a 1D blend tree on the parameter has more than 2 children. |
| **Float** | Radial puppet (slider) | Parameters with "Hue" in the name are picked automatically. |

A menu with more than 8 entries keeps 7 and adds a **Page 2** submenu, which chains on as Page 3, Page 4, and so on.

## Icons (optional)

Tick **Generate icons**, drag your avatar in from the scene, and every Bool toggle gets a 256x256
transparent icon, saved in `Generated/Icons/`.

For each toggle it finds the "on" clip (a Direct blend tree child, or the highest-threshold clip of a
1D tree on that parameter), poses a throwaway clone of the avatar with everything toggleable switched
off, then photographs only what that clip turns on. Your real avatar is never touched.

Limits: the camera is front-on and orthographic. Clips that only change materials or blendshapes
produce no icon.

## Parameter memory

The window shows the memory the new parameters cost (Bool 1 bit, Int and Float 8 bits). VRChat's
limit is 256 bits per avatar, and the tool does not enforce it.

## Status

Compiled and used in Unity on VRChat SDK 3.x. Not tested with every blend tree layout, so check the
result in the menu before uploading.
