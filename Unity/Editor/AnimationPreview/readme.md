# 🎚 MarkerTrackDrawer & IMarkerSource

## Overview

These two types form the **editor-only system** that renders and manages the interactive mini-timeline (marker track) above Unity’s built-in animation preview.
They are used by custom inspectors such as `AudioSMBEditor` to let designers author timed events visually without touching the Animation window.

---

## 🧩 `IMarkerSource`

**Purpose:**
Defines the minimal interface a host editor must expose so the `MarkerTrackDrawer` can query and modify its marker data.

The drawer never owns data; it simply operates on whatever list the host provides.

---

## 🎨 `MarkerTrackDrawer`

**Purpose:**
Draws the horizontal marker row aligned to the animation scrub bar and handles all user interaction.

**Responsibilities**

* Compute the marker-track rect from `AnimationPreviewDrawerUtil.GetTimelineRect(rect)`
* Render baseline and markers
* Handle add / select / drag / delete
* Keep all transient UI state (selection, drag positions) internal

**Usage**

```csharp
private MarkerTrackDrawer _markerTrack;

protected override void OnEnable()
{
    _markerTrack = new MarkerTrackDrawer(this); // ‘this’ implements IMarkerSource
}

public override void OnInspectorGUI()
{
    // draw normal inspector ...
    _markerTrack.Draw(previewRect);
}
```

**Right-side buttons:**
Placed above the playback-speed slider; use the region between the scrub bar’s right edge and the inspector’s right margin for “＋ / － / options” buttons.

---

### Notes

* Purely **editor-side**; no runtime references.
* Works with any data type implementing `IMarkerSource`, not just `AudioSMBEvent`.
* Designed to be lightweight and self-contained so editors stay clean.

---
