# Auto FX To Menu

A VRChat avatar tool that builds your Expression Parameters and Expressions Menus straight from your
FX controller, with paged menus (VRChat allows 8 controls per menu) and optional auto-generated icons
for toggles.

The package lives in [`Packages/com.opensource.autofxtomenu`](Packages/com.opensource.autofxtomenu);
see its README for how to use it.

## Install (VCC)

Add the listing to the VRChat Creator Companion: **Settings > Packages > Add Repository**, then paste

```
https://dragonboivrc.github.io/auto-fx-to-menu/index.json
```

Then add **Auto FX To Menu** to your project from the Manage Project screen. The listing page is at
<https://dragonboivrc.github.io/auto-fx-to-menu/>.

## Releasing

1. Bump `version` in `Packages/com.opensource.autofxtomenu/package.json`.
2. Run the **Build Release** workflow (Actions tab). It tags the version, makes the zip and
   `.unitypackage`, and publishes the GitHub release.
3. The **Build Repo Listing** workflow then rebuilds the listing site on GitHub Pages.

One-time setup: add a repository variable `PACKAGE_NAME` = `com.opensource.autofxtomenu`
(Settings > Secrets and variables > Actions > Variables), and set Pages to deploy from **GitHub Actions**.
