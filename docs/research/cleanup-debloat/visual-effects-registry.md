# Windows Visual Effects -- Complete Registry Reference

> Covers Windows 10 (all builds) and Windows 11 (through 24H2).
> All keys are per-user (HKCU) unless marked [Machine].

---

## 1. MASTER CONTROLS

### 1A. VisualFXSetting (Preset Selector)

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\VisualFXSetting
Type: DWORD
```

| Value | Meaning | What Windows Does |
|-------|---------|-------------------|
| 0 | Let Windows choose | Auto-enables/disables effects based on hardware score |
| 1 | Best appearance | Enables every effect |
| 2 | Best performance | Disables every effect |
| 3 | Custom | Uses whatever individual on/off flags you set |

> When set to 0, 1, or 2, Windows overwrites the individual flags below on
> every boot or when the user opens System Properties > Advanced > Performance.
> Set to 3 first, then write individual flags, or your changes revert.

---

### 1B. UserPreferencesMask (Global Bitmask)

```
HKCU\Control Panel\Desktop\UserPreferencesMask
Type: REG_BINARY  (length: always 94 bytes / 0x5E)
```

This single bitmask encodes the on/off state of dozens of visual effects.
Each bit controls one effect. Windows reads this at login and when
`SystemPropertiesPerformance.exe` is invoked.

#### Byte Layout (active bytes, offset from 0)

| Byte Offset | Bits (MSB->LSB) | Controls |
|-------------|-----------------|----------|
| 0 | b7..b0 | Peeking/Aero Peek, shadows, underline menu shortcuts |
| 1 | b7..b0 | Smooth-scroll, menu fade, selection fade, tooltip animation |
| 2 | b7..b0 | Cursor shadow, gradient caption, keyboard cues |
| 3 | b7..b0 | Font smoothing, tray animation, dialog animation |
| 4 | b7..b0 | Cursor shadow (dup), show shadows under windows, thumb animation |
| 5 | b7..b0 | Combo box animation, listview animation |
| 6 | b7..b0 | Menu animation type, gradient headers |
| 7 | b7..b0 | Embedded icon, active window tracking |
| 8 | b7..b0 | Hot tracking, animation direction |
| 9 | b7..b0 | Slide open combo boxes, fade tooltips |

#### Known Preset Values

**Best Performance (all effects off):**
```
90 12 01 80 10 00 00 00 (first 8 bytes)
```
Full binary: `90 12 01 80 10 00 00 00 00 00 00 00 00 00 00 00` ...
(Common representation as 94-byte hex string:)
```
90120180100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000
```

**Balanced (default "Let Windows choose" on a mid-range PC):**
```
9E 12 01 80 10 00 00 00 (first 8 bytes)
```
Full 94-byte hex:
```
9E120180100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000
```

**Best Appearance (all effects on):**
```
9E 12 01 80 10 00 00 00 3E 00 00 00 ...  (varies by Windows version)
```
Common Win10/11 full-appearence hex:
```
9E120180100000003E00000000000000000000000000000000000000000000000000000000000000000000000000000000000000
```

#### Practical .reg Format

```reg
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Control Panel\Desktop]
"UserPreferencesMask"=hex:90,12,01,80,10,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00
```

> **WARNING:** On Windows 11, the UserPreferencesMask length may vary.
> Always export your current value first with `reg export` before importing
> a preset. The "Best Performance" byte sequence above is the commonly
> documented one from Microsoft's own SystemPropertiesPerformance defaults.

---

## 2. INDIVIDUAL VISUAL EFFECTS

### Base Key for All Boolean Animation/UI Effects

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects
```

Each sub-key below lives under this path. Each has a value named `DefaultApplied`
(DWORD, set to 1) and one or more boolean DWORDs.

---

### 2.1 AnimateMinMax (Minimize/Maximize Animation)

```
...\VisualEffects\AnimateMinMax
Value: DWORD 0 or 1    (Enabled / Disabled)
```

- **What it does:** Animates windows when minimized to taskbar or restored. Without this, windows simply appear/disappear instantly.
- **Performance impact:** LOW
- **User-visible:** Yes -- very obvious when you maximize or minimize a window.
- **Recommended for performance:** 1 (keep enabled). The animation is lightweight and makes transitions feel smooth rather than jarring. Only disable if targeting every possible millisecond on very old hardware.

---

### 2.2 ComboBoxAnimation (ComboBox Slide Animation)

```
...\VisualEffects\ComboBoxAnimation
Value: DWORD 0 or 1
```

- **What it does:** When you click a drop-down combo box, the list slides open rather than appearing instantly.
- **Performance impact:** LOW
- **User-visible:** Somewhat -- only noticed when using drop-down lists.
- **Recommended:** 1 (keep enabled). Negligible GPU cost.

---

### 2.3 CursorShadow

```
...\VisualEffects\CursorShadow
Value: DWORD 0 or 1
```

- **What it does:** Draws a subtle drop shadow beneath the mouse cursor.
- **Performance impact:** LOW
- **User-visible:** Subtle. Most users do not consciously notice it.
- **Recommended:** 0 (disable for performance). The visual difference is minimal.

---

### 2.4 ControlPanelAnimation

```
...\VisualEffects\ControlPanelAnimation
Value: DWORD 0 or 1
```

- **What it does:** Enables transition animations when navigating between Control Panel pages.
- **Performance impact:** LOW
- **User-visible:** Only when using Control Panel (rare on modern Windows).
- **Recommended:** 0 (disable). Most users never notice.

---

### 2.5 CursorShadow (duplicate path variant)

Some builds reference this under:
```
...\VisualEffects\CursorShadow2
```
Same behavior as 2.3. Safe to set both.

---

### 2.6 DialogFadeEffect

```
HKCU\Control Panel\Desktop
Value: "DragFullWindows" DWORD  (see DragFullWindows below)
Additionally:
HKCU\Control Panel\Desktop
Value: "UserPreferencesMask" byte 3 bit 2 controls dialog fade
```

The dedicated flag for dialog fade-in:
```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects
```
Some Windows builds expose it as a `TranslucentDialog` or `ClientAreaAnimation` key.

- **What it does:** Dialog boxes (Save As, Open, etc.) fade in rather than appearing instantly.
- **Performance impact:** LOW
- **User-visible:** Mildly -- quick fade effect.
- **Recommended:** 0 (disable). Saves a trivial amount of GPU.

---

### 2.7 DragFullWindows (Drag Show Contents)

```
HKCU\Control Panel\Desktop
Value: "DragFullWindows"  (STRING, not DWORD)
  "1" = Show window contents while dragging (ON)
  "0" = Show only wireframe rectangle (OFF)
```

- **What it does:** When dragging a window, "1" redraws the full window in real-time. "0" shows only an outline rectangle (like Windows 95).
- **Performance impact:** MEDIUM on integrated GPUs / old hardware; LOW on dedicated GPUs.
- **User-visible:** Very noticeable -- dragging looks "old school" at 0.
- **Recommended:** 0 for performance on weak GPUs; 1 for modern hardware.

---

### 2.8 DropDownAnimation

```
...\VisualEffects\DropDownAnimation
Value: DWORD 0 or 1
```

- **What it does:** Animates the opening of drop-down menus (context menus, Start menu submenus). Items slide/fade in.
- **Performance impact:** LOW
- **User-visible:** Somewhat.
- **Recommended:** 1 (keep enabled). Trivial cost.

---

### 2.9 FontSmoothing (ClearType)

```
HKCU\Control Panel\Desktop
Value: "FontSmoothing"  (STRING)
  "2" = ClearType smoothing ON (default)
  "1" = Standard grayscale antialiasing
  "0" = No font smoothing
```

Also tracked under:
```
...\VisualEffects\FontSmoothing
Value: DWORD 0 or 1
```

- **What it does:** Enables ClearType subpixel rendering for LCD screens. Fonts look dramatically sharper.
- **Performance impact:** VERY LOW (CPU-based glyph rendering, cached after first draw)
- **User-visible:** EXTREMELY noticeable. Without it, text looks jagged and ugly on LCDs.
- **Recommended:** 1 / "2" (KEEP ENABLED). The performance cost is negligible and the readability improvement is enormous. Disabling this is almost never worthwhile.

---

### 2.10 ListviewAlpha (ListView Alpha Blending)

```
...\VisualEffects\ListviewAlpha
Value: DWORD 0 or 1
```

- **What it does:** Enables semi-transparent selection rectangles in list views (Explorer, Details view selections). Without it, the selection is solid color.
- **Performance impact:** LOW
- **User-visible:** Mildly -- the selection highlight is slightly translucent.
- **Recommended:** 0 (disable for performance). Minimal visual improvement.

---

### 2.11 ListviewShadow (Drop Shadow on Lists)

```
...\VisualEffects\ListviewShadow
Value: DWORD 0 or 1
```

Also in:
```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced
Value: "ListviewShadow"  DWORD 0 or 1
```

- **What it does:** Adds a subtle drop shadow behind list view items.
- **Performance impact:** LOW
- **User-visible:** Very subtle. Most users never notice.
- **Recommended:** 0 (disable for performance).

---

### 2.12 MenuAnimation (Menu Slide/Fade)

```
...\VisualEffects\MenuAnimation
Value: DWORD 0 or 1
```

- **What it does:** Menus slide or fade open instead of appearing instantly.
- **Performance impact:** LOW
- **User-visible:** Yes -- menus look "alive" when animated.
- **Recommended:** 1 (keep enabled). Lightweight effect that improves perceived quality.

---

### 2.13 MenuShowDelay (Menu Appear Delay)

```
HKCU\Control Panel\Desktop
Value: "MenuShowDelay"  (STRING)
  Default: "400" (milliseconds)
  "0" = Instant menu appearance
  "100" = Fast (100ms)
  "400" = Normal
  "2000" = Slow
```

- **What it does:** Controls how long (in ms) the mouse must hover before a cascading submenu opens.
- **Performance impact:** NONE (this is a timing value, not a rendering effect)
- **User-visible:** Yes -- affects perceived snappiness of menus.
- **Recommended:** "0" or "50" for performance feel. Menus pop open instantly. On slow machines, "200" prevents accidental menu cascades.

---

### 2.14 SelectionFade (Selection Fade Effect)

```
...\VisualEffects\SelectionFade
Value: DWORD 0 or 1
```

- **What it does:** When you click an item and move the mouse away, the selection highlight briefly fades out rather than disappearing instantly.
- **Performance impact:** VERY LOW
- **User-visible:** Subtle.
- **Recommended:** 0 (disable). Negligible visual improvement.

---

### 2.15 ShowInfoTip (Tooltip Info Tips)

```
...\VisualEffects\ShowInfoTip
Value: DWORD 0 or 1
```

- **What it does:** Shows tooltip information when hovering over items (file details, button descriptions, etc.).
- **Performance impact:** VERY LOW (tooltips are lightweight)
- **User-visible:** Yes -- this controls whether tooltips appear at all.
- **Recommended:** 1 (keep enabled). This is a usability feature, not a cosmetic effect. Disabling it reduces discoverability.

---

### 2.16 ShowShadows (Window Drop Shadows)

```
...\VisualEffects\ShowShadows
Value: DWORD 0 or 1
```

- **What it does:** Draws drop shadows behind windows, making them visually separate from the desktop background.
- **Performance impact:** LOW-MEDIUM (compositor must render shadow layer per window)
- **User-visible:** Moderately -- windows look "flat" without shadows.
- **Recommended:** 0 on old/integrated GPU hardware; 1 on modern systems. On Windows 10/11 with DWM active, this effect is essentially free since the compositor already renders per-window.

---

### 2.17 ShowThumbnails (Thumbnail Previews)

```
...\VisualEffects\ShowThumbnails
Value: DWORD 0 or 1
```

Also exposed in:
```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced
Value: "IconsOnly"  DWORD 0 or 1  (inverted logic: 0 = show thumbnails)
```

- **What it does:** In Explorer, shows preview thumbnails of images, videos, documents instead of generic file-type icons.
- **Performance impact:** MEDIUM-HIGH on slow drives / network shares (must read file contents to generate thumbnails); LOW on SSDs with local files.
- **User-visible:** VERY noticeable -- Explorer looks much richer with thumbnails.
- **Recommended:** 1 (keep enabled) on modern SSD systems. Disable only if browsing large network shares or on very slow storage.

---

### 2.18 SmoothEdges (Font Smoothing Duplicate)

This is an alias for FontSmoothing (2.9). Same registry path:
```
HKCU\Control Panel\Desktop
Value: "FontSmoothing"  "2"
```

---

### 2.19 SmoothScreenFontCaret (Smooth Caret Animation)

```
HKCU\Control Panel\Desktop
Value: "SmoothScreenFontCaret"  (STRING or DWORD depending on build)
  "1" = Smooth caret animation in text editors
  "0" = Instant caret movement
```

- **What it does:** Animates the text cursor (caret) smoothly between positions in text fields instead of jumping instantly.
- **Performance impact:** VERY LOW
- **User-visible:** Subtle -- noticed in Notepad, Word, etc.
- **Recommended:** 0 (disable). The animation is distracting for fast typists and saves negligible resources.

---

### 2.20 ThemeEffects / TaskbarAnimations

```
...\VisualEffects\TaskbarAnimations
Value: DWORD 0 or 1
```

- **What it does:** Enables taskbar button animations (fading, sliding when apps open/close/minimize).
- **Performance impact:** LOW
- **User-visible:** Yes -- taskbar buttons look static without it.
- **Recommended:** 1 (keep enabled). Negligible cost on modern hardware.

Also related:
```
HKCU\Software\Microsoft\Windows\DWM
Value: "EnableAeroPeek"  DWORD 0 or 1
```
See section 5 below.

---

### 2.21 TooltipAnimation

```
...\VisualEffects\TooltipAnimation
Value: DWORD 0 or 1
```

- **What it does:** Tooltips fade in/out and slide when appearing.
- **Performance impact:** VERY LOW
- **User-visible:** Mildly.
- **Recommended:** 0 (disable). Tooltips appear slightly faster without animation.

---

## 3. PERFORMANCE IMPACT SUMMARY TABLE

| Effect | Key | Perf Impact | Noticeable? | Recommended (Perf) |
|--------|-----|-------------|-------------|---------------------|
| AnimateMinMax | AnimateMinMax | Low | Yes | Enable |
| ComboBoxAnimation | ComboBoxAnimation | Low | Somewhat | Enable |
| CursorShadow | CursorShadow | Low | Subtle | Disable |
| ControlPanelAnimation | ControlPanelAnimation | Low | Rarely | Disable |
| DialogFadeEffect | (UserPreferencesMask bit) | Low | Mildly | Disable |
| DragFullWindows | DragFullWindows | Med (iGPU) | Very | Disable on iGPU |
| DropDownAnimation | DropDownAnimation | Low | Somewhat | Enable |
| FontSmoothing | FontSmoothing | Very Low | Extremely | **Enable** |
| ListviewAlpha | ListviewAlpha | Low | Mildly | Disable |
| ListviewShadow | ListviewShadow | Low | Very subtle | Disable |
| MenuAnimation | MenuAnimation | Low | Yes | Enable |
| MenuShowDelay | MenuShowDelay | None | Yes | 0 or 50 |
| SelectionFade | SelectionFade | Very Low | Subtle | Disable |
| ShowInfoTip | ShowInfoTip | Very Low | Yes | **Enable** |
| ShowShadows | ShowShadows | Low-Med | Moderately | Enable (modern) |
| ShowThumbnails | ShowThumbnails | Med-High (HDD) | Very | Enable (SSD) |
| SmoothScreenFontCaret | SmoothScreenFontCaret | Very Low | Subtle | Disable |
| TaskbarAnimations | TaskbarAnimations | Low | Yes | Enable |
| TooltipAnimation | TooltipAnimation | Very Low | Mildly | Disable |

---

## 4. EXPLORER ADVANCED SETTINGS

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced
```

| Value Name | Type | Description | Recommended |
|-----------|------|-------------|-------------|
| `TaskbarSmallIcons` | DWORD | Small taskbar icons (1=small, 0=normal) | 0 (normal) |
| `ListviewShadow` | DWORD | Shadow behind list items (1=on, 0=off) | 0 |
| `IconsOnly` | DWORD | No thumbnails, icons only (1=icons, 0=thumbs) | 0 |
| `LaunchTO` | DWORD | Open File Explorer to This PC (1) or Quick Access (0) | Preference |
| `TaskbarSizeMove` | DWORD | Allow taskbar resizing/moving (1=on, 0=locked) | Preference |
| `TaskbarAnimations` | DWORD | Taskbar button animations (1=on, 0=off) | 1 |
| `EnableBalloonTips` | DWORD | Balloon notifications (1=on, 0=off) | Preference |
| `Start_TrackDocs` | DWORD | Track recent documents (1=on, 0=off) | Preference |
| `Start_TrackProgs` | DWORD | Track frequently used programs (1=on, 0=off) | Preference |
| `StartMenuAdminTools` | DWORD | Show Admin Tools in Start (1=on, 0=off) | Preference |
| `CueBannerAnimations` | DWORD | Animate search hints (1=on, 0=off) | 0 |
| `FolderContentsTip` | DWORD | Show folder size in tooltip (1=on, 0=off) | 0 |
| `ShowSecondsInSystemClock` | DWORD | Show seconds in taskbar clock (Win10 1903+) | Preference |
| `ShowCloudIconInThisPC` | DWORD | Show OneDrive in This PC (1=on, 0=off) | 0 |
| `Hidden` | DWORD | Show hidden files (1=show, 2=hide) | Preference |
| `HideFileExt` | DWORD | Hide file extensions (1=hide, 0=show) | 0 |

---

## 5. DWM (Desktop Window Manager) SETTINGS

```
HKCU\Software\Microsoft\Windows\DWM
```

| Value Name | Type | Description | Recommended |
|-----------|------|-------------|-------------|
| `EnableAeroPeek` | DWORD | Peek at desktop on hover (1=on, 0=off) | 0 (perf) |
| `AlwaysHibernateThumbnails` | DWORD | Keep thumbnails in memory when minimized (1=on, 0=off) | 0 (perf) |
| `Composition` | DWORD | Force DWM composition (1=on, 0=off, 2=default) | 2 |
| `EnableWindowColorization` | DWORD | Window color tinting (1=on, 0=off) | Preference |
| `AccentColor` | DWORD | Accent color value (ARGB hex) | Preference |
| `ColorizationColor` | DWORD | Window chrome color (ARGB hex) | Preference |
| `ColorizationBlurBalance` | DWORD | Blur balance (0-100) | Preference |
| `ColorizationGlassAttribute` | DWORD | Glass appearance (1=opaque, 0=transparent) | Preference |
| `Opacity` | DWORD | Window border opacity (1-255) | Preference |
| `AccentPolicy` | DWORD | Accent policy mode | Preference |
| `UseWindowPreview` | DWORD | Live taskbar previews (1=on, 0=off) | 1 |
| `RoundedCorners` | DWORD | Windows 11 rounded corners (1=on, 0=off) -- Win11 only | Preference |

---

## 6. SYSTEM EFFECTS / THEME REGISTRY PATHS

### 6A. SystemParametersInfo-equivalent Keys

```
HKCU\Control Panel\Desktop
```

| Value Name | Type | Description |
|-----------|------|-------------|
| `UserPreferencesMask` | REG_BINARY | Master visual effects bitmask (see section 1B) |
| `DragFullWindows` | STRING | "1" or "0" -- full window drag |
| `FontSmoothing` | STRING | "0","1","2" -- font smoothing type |
| `FontSmoothingOrientation` | STRING | LCD subpixel order (1=RGB, 2=BGR, 3=VRGB, 4=VBGR) |
| `FontSmoothingGamma` | STRING | Smoothing gamma (clear type tuning) |
| `MenuShowDelay` | STRING | Menu hover delay in ms |
| `ScreenSaverIsSecure` | STRING | Lock screen on screensaver exit |
| `CursorBlinkRate` | STRING | Cursor blink rate in ms |
| `SmoothScreenFontCaret` | STRING | Smooth caret in text fields |
| `CursorBlinkRate` | STRING | "530" default, "0" = no blink |
| `GranularUIConstraints` | STRING | UI constraint values |

### 6B. WindowMetrics (Sizing & Spacing)

```
HKCU\Control Panel\Desktop\WindowMetrics
```

| Value Name | Type | Description |
|-----------|------|-------------|
| `ScrollHeight` | STRING | Scrollbar height (negative, in twips) |
| `ScrollWidth` | STRING | Scrollbar width |
| `CaptionHeight` | STRING | Title bar height |
| `CaptionWidth` | STRING | Title bar width |
| `IconSpacing` | STRING | Horizontal icon spacing |
| `IconVerticalSpacing` | STRING | Vertical icon spacing |
| `ShellIconSize` | DWORD | Icon size (16, 32, 48, 64, etc.) |

### 6C. Color and Theme

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects
HKCU\Software\Microsoft\Windows\DWM
HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
```

Personalize key:
```
HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
```

| Value Name | Type | Description |
|-----------|------|-------------|
| `AppsUseLightTheme` | DWORD | Light mode (1) vs dark mode (0) |
| `SystemUsesLightTheme` | DWORD | Taskbar/Start light (1) vs dark (0) |
| `ColorPrevalence` | DWORD | Use accent color on Start/taskbar (1=on) |
| `Transparency` | DWORD | Transparency effects (1=on, 0=off) |
| `EnableTransparency` | DWORD | Same as above (Win11) |

---

## 7. PRESET .REG FILES

### 7A. Best Performance Preset

```reg
Windows Registry Editor Version 5.00

; === VISUAL FX: BEST PERFORMANCE ===
; Sets master mode to "Best Performance" (2)
[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects]
"VisualFXSetting"=dword:00000002

; UserPreferencesMask - all effects OFF
[HKEY_CURRENT_USER\Control Panel\Desktop]
"UserPreferencesMask"=hex:90,12,01,80,10,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00

; Drag only outline
"DragFullWindows"="0"

; Fast menu show
"MenuShowDelay"="0"

; Disable ClearType (not recommended)
; "FontSmoothing"="0"

; Individual effects OFF
[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\AnimateMinMax]
"DefaultApplied"=dword:00000001

[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\ComboBoxAnimation]
"DefaultApplied"=dword:00000001

[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\MenuAnimation]
"DefaultApplied"=dword:00000001

; DWM tweaks
[HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM]
"EnableAeroPeek"=dword:00000000
"AlwaysHibernateThumbnails"=dword:00000000

; Explorer tweaks
[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced]
"TaskbarSmallIcons"=dword:00000000
"ListviewShadow"=dword:00000000
"TaskbarAnimations"=dword:00000000
"IconsOnly"=dword:00000000

; Transparency OFF
[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize]
"Transparency"=dword:00000000
"EnableTransparency"=dword:00000000
```

### 7B. Best Appearance Preset

```reg
Windows Registry Editor Version 5.00

; === VISUAL FX: BEST APPEARANCE ===
[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects]
"VisualFXSetting"=dword:00000001

[HKEY_CURRENT_USER\Control Panel\Desktop]
"UserPreferencesMask"=hex:9E,12,01,80,10,00,00,00,3E,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00

"DragFullWindows"="1"
"MenuShowDelay"="400"
"FontSmoothing"="2"
"SmoothScreenFontCaret"="1"

[HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM]
"EnableAeroPeek"=dword:00000001
"AlwaysHibernateThumbnails"=dword:00000001

[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced]
"ListviewShadow"=dword:00000001
"TaskbarAnimations"=dword:00000001
"IconsOnly"=dword:00000000

[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize]
"Transparency"=dword:00000001
"EnableTransparency"=dword:00000001
```

### 7C. Balanced Preset (Recommended Default)

```reg
Windows Registry Editor Version 5.00

; === VISUAL FX: BALANCED ===
[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects]
"VisualFXSetting"=dword:00000003

[HKEY_CURRENT_USER\Control Panel\Desktop]
"UserPreferencesMask"=hex:9E,12,01,80,10,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,00,\
  00,00,00,00,00,00,00

"DragFullWindows"="1"
"MenuShowDelay"="400"
"FontSmoothing"="2"

[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\AnimateMinMax]
"DefaultApplied"=dword:00000001

[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\ComboBoxAnimation]
"DefaultApplied"=dword:00000001

[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\MenuAnimation]
"DefaultApplied"=dword:00000001

[HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM]
"EnableAeroPeek"=dword:00000001
"AlwaysHibernateThumbnails"=dword:00000000

[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced]
"ListviewShadow"=dword:00000000
"TaskbarAnimations"=dword:00000001
"IconsOnly"=dword:00000000

[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize]
"Transparency"=dword:00000001
"EnableTransparency"=dword:00000001
```

---

## 8. POWERSHELL COMMANDS

### Apply Settings via PowerShell (No .reg file needed)

```powershell
# === SET MASTER MODE ===
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" `
    -Name "VisualFXSetting" -Value 2  # 0=Choose, 1=Best, 2=Perf, 3=Custom

# === SET UserPreferencesMask ===
$mask = [byte[]]@(0x90,0x12,0x01,0x80,0x10,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00)
Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "UserPreferencesMask" -Value $mask

# === DragFullWindows ===
Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "DragFullWindows" -Value "0"

# === MenuShowDelay ===
Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "MenuShowDelay" -Value "0"

# === Font Smoothing ===
Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "FontSmoothing" -Value "2"

# === Individual Effects ===
$visPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"

# Toggle individual effects on/off:
Set-ItemProperty -Path "$visPath\AnimateMinMax" -Name "DefaultApplied" -Value 1
Set-ItemProperty -Path "$visPath\ComboBoxAnimation" -Name "DefaultApplied" -Value 1
Set-ItemProperty -Path "$visPath\MenuAnimation" -Name "DefaultApplied" -Value 1

# === DWM ===
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\DWM" -Name "EnableAeroPeek" -Value 0
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\DWM" -Name "AlwaysHibernateThumbnails" -Value 0

# === Explorer Advanced ===
$advPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
Set-ItemProperty -Path $advPath -Name "ListviewShadow" -Value 0
Set-ItemProperty -Path $advPath -Name "TaskbarAnimations" -Value 0
Set-ItemProperty -Path $advPath -Name "IconsOnly" -Value 0

# === Transparency ===
$themePath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
Set-ItemProperty -Path $themePath -Name "Transparency" -Value 0
Set-ItemProperty -Path $themePath -Name "EnableTransparency" -Value 0

# === REFRESH EXPLORER (apply without reboot) ===
# Forces Explorer to re-read visual effect settings
$code = @'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class VisualFX {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SystemParametersInfo(int uAction, int uParam, IntPtr lpvParam, int fuWinIni);
    public const int SPI_SETUIEFFECTS = 0x103E;
    public const int SPIF_UPDATEINIFILE = 0x01;
    public const int SPIF_SENDCHANGE = 0x02;
    public static void Refresh() {
        // Toggle to force refresh
        SystemParametersInfo(SPI_SETUIEFFECTS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }
}
"@
[VisualFX]::Refresh()
'@
Invoke-Expression $code
```

### Quick Apply Script (Toggle Individual Effects)

```powershell
# Toggle a specific visual effect on/off
# Usage: .\Toggle-VisualFX.ps1 -Effect "MenuAnimation" -Enable $false

param(
    [Parameter(Mandatory=$true)]
    [string]$Effect,
    [bool]$Enable = $false
)

$visPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"
$value = if ($Enable) { 1 } else { 0 }

# Handle special cases with different registry locations
switch ($Effect) {
    "DragFullWindows" {
        Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "DragFullWindows" -Value $(if ($Enable) {"1"} else {"0"})
    }
    "MenuShowDelay" {
        param($Delay = "0")
        Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "MenuShowDelay" -Value $Delay
    }
    "FontSmoothing" {
        Set-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name "FontSmoothing" -Value $(if ($Enable) {"2"} else {"0"})
    }
    "Transparency" {
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" -Name "Transparency" -Value $value
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" -Name "EnableTransparency" -Value $value
    }
    "EnableAeroPeek" {
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\DWM" -Name "EnableAeroPeek" -Value $value
    }
    default {
        # Standard VisualEffects sub-key
        $effectPath = Join-Path $visPath $Effect
        if (Test-Path $effectPath) {
            Set-ItemProperty -Path $effectPath -Name "DefaultApplied" -Value $value
            Write-Host "$Effect set to $(if ($Enable) {'ON'} else {'OFF'})"
        } else {
            # Try creating the key
            New-Item -Path $effectPath -Force | Out-Null
            New-ItemProperty -Path $effectPath -Name "DefaultApplied" -Value $value -PropertyType DWORD -Force | Out-Null
            Write-Host "$Effect created and set to $(if ($Enable) {'ON'} else {'OFF'})"
        }
    }
}
```

---

## 9. BATCH COMMAND FILE (.CMD)

For use without PowerShell:

```batch
@echo off
REM === WINDOWS VISUAL EFFECTS - BEST PERFORMANCE ===
REM Run as the target user (not elevated needed for HKCU)

echo Applying Best Performance visual settings...

REM Master control
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" /v VisualFXSetting /t REG_DWORD /d 2 /f

REM UserPreferencesMask (all effects off)
reg add "HKCU\Control Panel\Desktop" /v UserPreferencesMask /t REG_BINARY /d 90120180100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000 /f

REM Individual settings
reg add "HKCU\Control Panel\Desktop" /v DragFullWindows /t REG_SZ /d "0" /f
reg add "HKCU\Control Panel\Desktop" /v MenuShowDelay /t REG_SZ /d "0" /f
reg add "HKCU\Control Panel\Desktop" /v FontSmoothing /t REG_SZ /d "2" /f

REM DWM
reg add "HKCU\Software\Microsoft\Windows\DWM" /v EnableAeroPeek /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\Windows\DWM" /v AlwaysHibernateThumbnails /t REG_DWORD /d 0 /f

REM Explorer
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v ListviewShadow /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v TaskbarAnimations /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v IconsOnly /t REG_DWORD /d 0 /f

REM Transparency
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" /v Transparency /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" /v EnableTransparency /t REG_DWORD /d 0 /f

REM Restart Explorer to apply
taskkill /f /im explorer.exe
start explorer.exe

echo Done.
pause
```

---

## 10. RECOMMENDED "SMART PERFORMANCE" CONFIGURATION

This is the optimal balance -- maximum perceived smoothness with minimal GPU cost:

```
VisualFXSetting = 3 (Custom)
DragFullWindows = 0 (wireframe -- biggest perf win on iGPU)
MenuShowDelay = 50 (fast but not instant)
FontSmoothing = 2 (always keep ClearType)
ShowInfoTip = 1 (keep -- usability feature)
AnimateMinMax = 1 (keep -- feels smooth)
ComboBoxAnimation = 1 (keep -- trivial cost)
MenuAnimation = 1 (keep -- trivial cost)
TaskbarAnimations = 1 (keep -- trivial cost)
CursorShadow = 0 (save -- nobody notices)
ListviewAlpha = 0 (save -- minimal visual gain)
ListviewShadow = 0 (save -- subtle)
SelectionFade = 0 (save -- very subtle)
SmoothScreenFontCaret = 0 (save -- can be distracting)
TooltipAnimation = 0 (save -- subtle)
ControlPanelAnimation = 0 (save -- rarely used)
DialogFadeEffect = 0 (save -- trivial)
EnableAeroPeek = 0 (save -- compositor cost per frame)
AlwaysHibernateThumbnails = 0 (save -- RAM usage)
Transparency = 0 (save -- compositor overlay cost)
ShowThumbnails = 1 (keep on SSD -- usability)
```

---

## 11. IMPORTANT NOTES

1. **UserPreferencesMask length varies.** Windows 10 and 11 may use different byte counts (typically 94 bytes). Always export your current value first:
   ```powershell
   reg export "HKCU\Control Panel\Desktop" desktop_backup.reg
   ```

2. **VisualFXSetting=0,1,2 overrides individual flags.** If you set it to 0/1/2, Windows rewrites individual effect keys on next boot. Must be set to 3 for custom control.

3. **Some effects are only available in certain Windows builds.** Windows 11 24H2 may add or remove registry keys compared to Windows 10 22H2.

4. **DWM (Desktop Window Manager) cannot be fully disabled** on Windows 10/11. The composition value controls colorization, not whether DWM runs. DWM is mandatory since Windows 8.

5. **Explorer restart** is required for many changes to take effect without a full reboot:
   ```powershell
   Stop-Process -Name explorer -Force; Start-Process explorer
   ```

6. **Group Policy** can override user settings. Check:
   ```
   HKLM\Software\Policies\Microsoft\Windows\DWM
   HKCU\Software\Policies\Microsoft\Windows\Explorer
   ```

7. **Backup first.** Before applying any .reg file, always export the current state:
   ```powershell
   reg export "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" visual_backup.reg
   reg export "HKCU\Software\Microsoft\Windows\DWM" dwm_backup.reg
   reg export "HKCU\Control Panel\Desktop" desktop_backup.reg
   ```
