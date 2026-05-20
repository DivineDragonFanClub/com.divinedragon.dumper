# Changelog

## 1.1.2

- Add a hash map to use for replacing `path_` bone names that AssetRipper creates.
- Usually this repair triggers automatically on import of any `.anim` files, but a menu option allows it to be run manually on any previously imported animations. 

## 1.1.1

- Automated the lip and mouth mask fixing documented by BadAtGames26 at https://github.com/DivineDragonFanClub/Lythos/wiki/Fixing-Lip-Sync-Animations-for-uHeads. The  AssetPostprocessor script will automatically run when a uHead is imported.
- Added menu items for manually fixing heads that may be useful for people who imported heads before this tool, or have heads they created that they want to set up the masking for.