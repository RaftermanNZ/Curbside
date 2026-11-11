# Compatibility review for 0.6.0

This is a source and installed-assembly review, not runtime certification of every listed mod. The review screened 565 source repositories, refreshed 22 relevant source checkouts, and inspected seven playsets plus 359 cached managed assemblies. Source HEAD is not necessarily identical to the installed binary.

- **Traffic / TTE:** controller phases and masks remain owned by the native or traffic-control system. Protected-turn visuals can still overlap custom signal ownership. The existing TTE double-light report is not declared universally fixed.
- **Town Road Lane and other lane generators:** replacement secondary-lane systems can affect generation order. An ordering anchor for the native system does not automatically order every foreign replacement.
- **Move It and object editors:** tools can change or clear native update/override markers. Moving a generated signal manually can conflict with network-driven placement; avoid two owners controlling the same pose.
- **Placemaker and driving mods:** static review did not establish Curbside as the cause of the vague compatibility reports. Installed and publicly available source versions differ in some cases.
- **Other signal appearance mods:** overlapping ownership at one junction remains a conflict risk. Test with a single appearance owner before combining them.
- **Playsets:** stable/beta or original/hotfix duplicates were present in some reviewed playsets. Older cached mods also contained references to game members absent from the current SDK. Those findings do not prove an active runtime failure.

Existing saved Curbside component layouts and the assembly identity are unchanged. Keep a backup when changing code or asset versions. Brief save-time default-marking flashes remain possible on the current native module profile.

The release does not modify playsets, third-party code or native collision services. Native rendering, save/load round trips, performance and gameplay with a particular combination still need in-game verification.
