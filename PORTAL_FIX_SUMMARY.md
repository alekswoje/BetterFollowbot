# Portal Detection Fix Summary

## Issue Description
When transitioning from hideout to maps, the bot sometimes fails to follow the leader because:
1. It doesn't draw UI on map portals
2. It can't find the portal to click on
3. It falls back to using the blue swirly (party teleport button) instead
4. The issue is fixed by refreshing the area (because by then portal labels have loaded)
5. Occurs approximately every 4 maps

## Root Cause
Map device portals (MultiplexPortal entities) spawn but their labels may not be fully visible/loaded immediately. The code was using strict visibility checks that would fail for newly spawned portals:
- Old check: `x.IsVisible && x.Label.IsVisible`
- This caused portals to be invisible to the bot until their labels fully loaded

## Solution
Relaxed visibility requirements specifically for MultiplexPortal entities while keeping strict checks for other portal types:
- MultiplexPortal: Only require `x.Label != null && x.Label.IsValid`
- Other portals: Keep strict checks `x.IsVisible && x.Label.IsVisible`

## Files Modified

### 1. PortalManager.cs - FindMatchingPortal()
**Lines: 161-242**

Added:
- Debug logging for total ItemsOnGroundLabels count
- Debug logging for each portal found (label, metadata, entity type, distance, visibility)
- Debug logging for MultiplexPortal entities that were filtered out (to diagnose why they weren't matched)
- Relaxed visibility requirements for MultiplexPortal in the initial filtering
- Step-by-step portal matching debug logs

### 2. AutoPilot.cs - Portal Drawing Code
**Lines: 1239-1258**

Changed portal filtering from strict visibility checks to:
- Check if metadata contains "multiplexportal"
- If yes: Only require `x.Label != null && x.Label.IsValid`
- If no: Use normal strict visibility checks

This ensures the UI draws on map portals even when they're not fully loaded.

### 3. AutoPilot.cs - GetBestPortalLabel()
**Lines: 480-497**

Same fix as #2 - relaxed visibility requirements for MultiplexPortal entities in the portal detection logic.

## Debugging Added

The new debug logs will show:

1. **Total portal count**: How many ItemsOnGroundLabels exist
2. **Filtered portal count**: How many passed the portal detection filter
3. **Portal details**: For each portal found (up to 10):
   - Label text
   - Metadata path
   - Entity type
   - Distance to player
   - IsVisible status
   - Label.IsVisible status

4. **Unfiltered MultiplexPortal details**: If no portals were found, shows MultiplexPortals that exist but were filtered out, with their visibility status

5. **Portal matching step-by-step**: For each portal:
   - Direct match result
   - Variation match result
   - All variations being checked

## Testing Instructions

1. Load the bot and go to hideout
2. Have leader open a map
3. Watch the logs when the issue occurs
4. Look for these debug messages:
   - `PORTAL DEBUG: Total ItemsOnGroundLabels count: X`
   - `PORTAL DEBUG: Found X portal objects after filtering`
   - `PORTAL DEBUG [0]: Label='...', Metadata='...', Type=...`
   - `PORTAL DEBUG (Unfiltered) [0]: Label='...', HasLabel=..., LabelValid=...`

5. If portals are still not being found:
   - Check if MultiplexPortal entities exist but have `LabelValid=False`
   - Check if the label text doesn't match the expected zone name
   - Share the logs with the developer

## Expected Result

- Map portals should now be detected immediately when spawned
- UI should draw on the portals (yellow text showing "Portal: [name]")
- Bot should click on the actual portal instead of using the blue swirly
- Issue should occur much less frequently or not at all

## If Issue Persists

If you still see the issue after this fix, the new debug logs will tell us:
1. Are MultiplexPortal entities being detected at all?
2. Are their labels valid but not visible?
3. Does the label text match what we're searching for?
4. Is there a timing issue where portals spawn later than expected?

