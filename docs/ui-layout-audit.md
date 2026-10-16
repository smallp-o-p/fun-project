# UI layout audit (work in progress)

Base: master `3518e24` (includes #35). This separate pass changes the design viewport and launch window to 1920x1080, retaining canvas_items/expand stretching.

## Scope inspected

All authored UI scenes under scenes/battle, scenes/dialogue, scenes/geoscape and scenes/ui, their runtime scripts, main.tscn and the debug hosts. Scene and script inspection covers battle HUD/viewport forwarding, event resolution/dialogue overlays, mission preparation/results, prediction markets, roster/unit equipment/skills, captivity, engineering/manufacturing, research and staff hiring. There is no separate main-menu UI in this revision.

## Current changes

Replace HUD and research/hiring pixel rectangles with fractional anchors; use container ratios for campaign columns and model areas; bound runtime manufacturing-name width proportionally; replace mission results' fixed content width with anchors. Remove redundant initial dimensions from stretched model/debug viewports.

## Deliberate fixed values

Fonts, theme padding/separation, borders and icon/art geometry remain explicit. Button and row hit-area minimum heights remain. Squad card minimum width preserves readable horizontally scrollable cards. Region/event shapes and coordinates are map-space authored geometry, transformed by the camera rather than viewport layout.

## Validation and remaining work

- Baseline Debug build passed with zero warnings/errors.
- Baseline rendered layout/dialogue selection: 84 passed, zero failed.
- First changed-layout rendered selection: 65 passed, four failed of 69.
- Known pending fixes: geoscape camera refits after aspect changes; obsolete 800x600/fixed summary-width test expectations; three labels accidentally re-encoded by the edit script; restore small roster column readability minima; typography and complete multi-resolution/input QA.
- Captured actual Godot framebuffer screenshots at 1920x1080 for geoscape, roster, unit equipment, skills, engineering, research/hiring and captivity. These are interim captures, not final visual sign-off.
- Format, final build/full tests, final visual review and smaller/aspect-ratio captures remain pending.

The desktop disconnected during correction. Inspection after reconnect confirmed those last corrections had not executed. No claims of final validation are made by the initial draft.
