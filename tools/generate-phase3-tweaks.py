"""Generate the Phase 3 catalogue additions.

Written as a script rather than by hand so all 27 definitions get the same key
order, the same spec blocks, and the same detect/apply/rollback/verify shape as
the58 already in the catalogue. Re-running it rewrites the additions; it does
not touch anything else.
"""
import json
import io
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def reg(id, name, description, category, subcategory, risk, evidence, key, value,
        target, default, rtype="DWORD", tags=(), min_build=0, absent=False):
    """A registry tweak: one value, written the same way detect reads it."""
    detect = {"registryKey": key, "registryValue": value,
              "expectedApplied": target}
    if absent:
        # Windows' default here is "the value does not exist", which cannot be
        # written as a default value without inventing one.
        detect["expectedAbsent"] = True
    else:
        detect["expectedDefault"] = default

    d = {
        "id": id,
        "name": name,
        "description": description,
        "category": category,
        "subcategory": subcategory,
        "risk": risk,
        "evidence": evidence,
    }
    if min_build:
        d["minBuild"] = min_build
    d.update({
        "tags": list(tags),
        "method": "Registry",
        "targetValue": target,
        "defaultValue": default,
        "params": {"registryKey": key, "registryValue": value, "registryType": rtype},
        "detect": detect,
        "apply": {"registryKey": key, "registryValue": value,
                  "registryData": target, "registryType": rtype},
        "rollback": {"registryKey": key, "registryValue": value,
                     "registryData": default, "registryType": rtype},
        "verify": {"registryKey": key, "registryValue": value,
                   "expectedApplied": target},
    })
    return d


EXPLORER_ADV = r"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
CDM = r"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
CLOUD_CU = r"HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent"
CLOUD_LM = r"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent"


visual = [
    reg(
        "visual.transparency.disabled",
        "Disable Transparency Effects",
        "Turns off the acrylic and blur Windows applies to the Start menu, taskbar "
        "and window frames (Themes\\Personalize\\EnableTransparency). Purely cosmetic: "
        "it removes a per-frame composition cost that is measurable mainly on "
        "integrated graphics, and it makes the desktop read as solid colour instead "
        "of frosted glass. Windows exposes the identical switch at Settings > "
        "Accessibility > Visual effects > Transparency effects.",
        "visual-effects", "transparency", "Safe", 5,
        r"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
        "EnableTransparency", "0", "1",
        tags=["visual-effects", "performance", "gpu"],
    ),
    reg(
        "visual.tooltipDelay",
        "Tooltip Delay - Reduced",
        "Sets how long the cursor must hover before a tooltip appears "
        "(Control Panel\\Desktop\\MouseHoverTimeout) from the 400 ms default to 50 ms, "
        "matching the menu delay already in the catalogue. It changes only how fast a "
        "tooltip arrives, never how long it stays. The value is a desktop setting "
        "rather than a policy, and it is written only once Windows has been asked to "
        "change it — an absent value is the 400 ms default.",
        "visual-effects", "input-delay", "Safe", 3,
        r"HKCU\Control Panel\Desktop", "MouseHoverTimeout", "50", "400",
        rtype="SZ", tags=["visual-effects", "responsiveness"],
    ),
    reg(
        "visual.taskbarAnimations.disabled",
        "Disable Taskbar Animations",
        "Stops the taskbar sliding and fading as icons appear, move and leave "
        "(Explorer\\Advanced\\TaskbarAnimations). The animation is composited on the "
        "GPU either way, so the win is a smaller one than it looks — but on a machine "
        "where every composited frame counts it is free to give up.",
        "visual-effects", "animations", "Safe", 5,
        EXPLORER_ADV, "TaskbarAnimations", "0", "1",
        tags=["visual-effects", "performance", "gpu"],
    ),
    reg(
        "visual.fontSmoothing.disabled",
        "Disable Font Smoothing",
        "Turns off font smoothing (Control Panel\\Desktop\\FontSmoothingType, the value "
        "Performance Options writes when you choose 'Adjust for best performance'). "
        "Text becomes visibly harder to read on any display where the pixel grid is "
        "visible, which is nearly all of them; the benefit exists only on hardware "
        "that spends measurable time rasterising glyphs. Windows keeps ClearType's own "
        "calibration alongside, so this is fully reversible.",
        "visual-effects", "font-smoothing", "Optional", 5,
        r"HKCU\Control Panel\Desktop", "FontSmoothingType", "0", "2",
        tags=["visual-effects", "performance"],
    ),
]

# The existing definition of this id claims to disable transparency and writes
# an unrelated value; the copy is rewritten to say what it actually does, and
# the evidence drops to 2 because the value appears in no shell binary on this
# build.
OLED_FIX = {
    "id": "visual.transparentEffects",
    "name": "Taskbar OLED Transparency",
    "description": "Raises the taskbar's background transparency so more of the "
                   "wallpaper shows through (HKLM\\...\\Explorer\\Advanced\\"
                   "UseOLEDTaskbarTransparency). A display preference aimed at OLED "
                   "panels, with no effect on frame rate. The value does not appear "
                   "in the Windows 11 shell binaries this catalogue is developed "
                   "against, so treat its effect as unverified. The switch that "
                   "actually turns transparency off is visual.transparency.disabled.",
    "category": "visual-effects",
    "subcategory": "transparency",
    "risk": "Optional",
    "evidence": 2,
    "tags": ["visual-effects", "display"],
    "method": "Registry",
    "targetValue": "1",
    "defaultValue": "0",
    "params": {
        "registryKey": r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
        "registryValue": "UseOLEDTaskbarTransparency",
        "registryType": "DWORD",
    },
    "detect": {
        "registryKey": r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
        "registryValue": "UseOLEDTaskbarTransparency",
        "expectedApplied": "1",
        "expectedDefault": "0",
    },
    "apply": {
        "registryKey": r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
        "registryValue": "UseOLEDTaskbarTransparency",
        "registryData": "1",
        "registryType": "DWORD",
    },
    "rollback": {
        "registryKey": r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
        "registryValue": "UseOLEDTaskbarTransparency",
        "registryData": "0",
        "registryType": "DWORD",
    },
    "verify": {
        "registryKey": r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
        "registryValue": "UseOLEDTaskbarTransparency",
        "expectedApplied": "1",
    },
}

explorer = [
    reg(
        "explorer.fileExtensions.show",
        "Show File Extensions",
        "Stops Explorer hiding the extension on file names (Explorer\\Advanced\\"
        "HideFileExt). Without it a file called invoice.pdf.exe is read as a PDF by "
        "anyone glancing at it. Folder Options > View offers the same checkbox, and "
        "it is a per-user setting, so nothing about the machine changes.",
        "explorer", "file-names", "Recommended", 5,
        EXPLORER_ADV, "HideFileExt", "0", "1",
        tags=["explorer", "usability", "security"],
    ),
    reg(
        "explorer.hiddenFiles.show",
        "Show Hidden Files and Folders",
        "Sets Explorer\\Advanced\\Hidden to 1 so entries marked hidden are painted in "
        "folder views. Note the long-standing quirk that trips everyone: 1 means show, "
        "2 means hide. Files keep their hidden attribute — this only changes whether "
        "the listing draws them. Folder Options > View offers the same choice.",
        "explorer", "folder-view", "Optional", 5,
        EXPLORER_ADV, "Hidden", "1", "2",
        tags=["explorer", "usability"],
    ),
    reg(
        "explorer.start.recentDocs",
        "Stop Listing Recent Documents in Start",
        "Clears Explorer\\Advanced\\Start_TrackDocs so Start and application Jump "
        "Lists stop listing recently opened files. It does not delete the documents, "
        "only the record of which were opened last. Windows offers the same switch at "
        "Settings > Personalization > Start > Show recently opened items in Jump "
        "Lists and Start.",
        "explorer", "start", "Recommended", 4,
        EXPLORER_ADV, "Start_TrackDocs", "0", "1",
        tags=["explorer", "privacy", "start"],
    ),
    reg(
        "explorer.launchTo.thisPc",
        "Open File Explorer to This PC",
        "Sets Explorer\\Advanced\\LaunchTo so a new Explorer window opens on This PC "
        "instead of Home. Home enumerates recent files and frequent folders every time "
        "a window opens; This PC lists drives. Folder Options > General > Open File "
        "Explorer to.",
        "explorer", "folder-view", "Optional", 4,
        EXPLORER_ADV, "LaunchTo", "2", "1",
        tags=["explorer", "performance", "usability"],
    ),
    reg(
        "explorer.compactMode",
        "Compact Folder Rows",
        "Sets Explorer\\Advanced\\UseCompactMode so list rows use compact padding and "
        "more entries fit on screen without scrolling. The same toggle lives in "
        "Explorer's View menu as Compact view. It is a layout preference, not a "
        "performance change.",
        "explorer", "folder-view", "Optional", 5,
        EXPLORER_ADV, "UseCompactMode", "1", "0",
        tags=["explorer", "usability"],
    ),
    reg(
        "explorer.syncProviderNotifications.disabled",
        "Disable Sync Provider Notifications",
        "Turns off the sync-provider tips Explorer draws over folder views "
        "(Explorer\\Advanced\\ShowSyncProviderNotifications) — the OneDrive and "
        "third-party storage prompts that appear under the file list. It removes "
        "promotional content and nothing else.",
        "explorer", "suggestions", "Safe", 5,
        EXPLORER_ADV, "ShowSyncProviderNotifications", "0", "1",
        tags=["explorer", "privacy", "ads"],
    ),
    reg(
        "explorer.thumbnails.iconsOnly",
        "Show Icons Instead of Thumbnails",
        "Sets Explorer\\Advanced\\IconsOnly so folders paint the file-type icon rather "
        "than extracting a preview image. Large folders, network shares and archives "
        "stop waiting on thumbnail generation to display. Folder Options > View > "
        "Always show icons, never thumbnails.",
        "explorer", "thumbnails", "Optional", 5,
        EXPLORER_ADV, "IconsOnly", "1", "0",
        tags=["explorer", "performance", "storage"],
    ),
    reg(
        "explorer.taskbar.widgets.disabled",
        "Hide the Widgets Taskbar Button",
        "Sets Explorer\\Advanced\\TaskbarDa to 0 so the Widgets button leaves the "
        "taskbar. The Widgets board itself remains reachable from its shortcut; this "
        "removes the button. Windows exposes the same toggle in Taskbar settings > "
        "Widgets.",
        "explorer", "taskbar", "Optional", 5,
        EXPLORER_ADV, "TaskbarDa", "0", "1",
        tags=["explorer", "taskbar", "distraction"],
    ),
    reg(
        "explorer.newsAndInterests.disabled",
        "Disable News and Interests",
        "Sets the AllowNewsAndInterests policy (SOFTWARE\\Policies\\Microsoft\\Dsh) to "
        "0, which removes the news and weather feed from the taskbar. Microsoft "
        "documents the policy in NewsAndInterests.admx, where disabling the policy "
        "corresponds to the value 0. This is machine-wide and needs elevation.",
        "explorer", "taskbar", "Recommended", 5,
        r"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", "0", "1",
        tags=["explorer", "taskbar", "distraction"],
    ),
    reg(
        "explorer.taskbar.alignLeft",
        "Align the Taskbar Left",
        "Sets Explorer\\Advanced\\TaskbarAl to 0 so the Windows 11 taskbar sits at the "
        "left edge rather than centred. A layout preference with no performance "
        "content; the value is absent on a default install because centred is the "
        "default.",
        "explorer", "taskbar", "Optional", 4,
        EXPLORER_ADV, "TaskbarAl", "0", "1",
        tags=["explorer", "taskbar"],
    ),
    reg(
        "explorer.classicContextMenu",
        "Classic Right-Click Menu",
        "Creates HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}"
        "\\InprocServer32 with an empty default value, which makes Windows 11 show the "
        "full context menu directly instead of collapsing it behind 'Show more "
        "options'. Needs build 22000 or later and an Explorer restart before it "
        "appears. Microsoft does not document this key; it is included because it is "
        "reversible and its effect is visible, not because it is supported — and the "
        "applied state is an empty value, which is why this one declares that absence "
        "is the default rather than inventing a number for it.",
        "explorer", "context-menu", "Optional", 3,
        r"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32",
        "", "", "",
        rtype="SZ", tags=["explorer", "usability"], min_build=22000, absent=True,
    ),
]

privacy = [
    reg(
        "privacy.searchWebSuggestions.disabled",
        "Stop Sending Search Box Keystrokes to the Web",
        "Enables the DisableSearchBoxSuggestions policy "
        "(SOFTWARE\\Policies\\Microsoft\\Windows\\Explorer) so the Windows search box "
        "returns local results only and no longer streams what you type to the web. "
        "Microsoft documents it in WindowsExplorer.admx. The trade is functional: a "
        "web query typed into the search box now produces no suggestions. Per-user.",
        "privacy", "search", "Recommended", 5,
        r"HKCU\SOFTWARE\Policies\Microsoft\Windows\Explorer",
        "DisableSearchBoxSuggestions", "1", "0",
        tags=["privacy", "search", "telemetry"],
    ),
    reg(
        "privacy.startSuggestions.disabled",
        "Stop Suggested Apps in Start",
        "Turns off suggested applications in the Start menu "
        "(ContentDeliveryManager\\SystemPaneSuggestionsEnabled). This is the per-user "
        "half of Windows' app recommendations; the machine-wide half, which also stops "
        "apps being installed, is privacy.cloudSuggestions.disabled.",
        "privacy", "ads", "Recommended", 4,
        CDM, "SystemPaneSuggestionsEnabled", "0", "1",
        tags=["privacy", "ads", "start"],
    ),
    reg(
        "privacy.feedbackPrompts.disabled",
        "Stop Windows Asking for Feedback",
        "Enables the DoNotShowFeedbackNotifications policy "
        "(SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection), which stops Windows "
        "asking for feedback after updates and sign-ins. Microsoft documents it in "
        "FeedbackNotifications.admx. Machine-wide, so it needs elevation.",
        "privacy", "feedback", "Safe", 5,
        r"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
        "DoNotShowFeedbackNotifications", "1", "0",
        tags=["privacy", "feedback"],
    ),
    reg(
        "privacy.tips.disabled",
        "Disable Windows Tips and Suggestions",
        "Enables the DisableSoftLanding policy "
        "(SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent), which turns off the "
        "Windows welcome experience, tips and suggested content shown in Start and on "
        "the lock screen. Microsoft documents it in CloudContent.admx. Machine-wide, "
        "so it needs elevation.",
        "privacy", "tips", "Safe", 5,
        CLOUD_LM, "DisableSoftLanding", "1", "0",
        tags=["privacy", "ads", "tips"],
    ),
    reg(
        "privacy.tailoredExperiences.disabled",
        "Disable Tailored Experiences",
        "Enables the DisableTailoredExperiencesWithDiagnosticData policy "
        "(SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent), so Windows stops using "
        "diagnostic data to choose tips, offers and recommendations for this account. "
        "Microsoft documents it in CloudContent.admx. Per-user.",
        "privacy", "personalisation", "Safe", 5,
        CLOUD_CU, "DisableTailoredExperiencesWithDiagnosticData", "1", "0",
        tags=["privacy", "ads", "data-collection"],
    ),
    reg(
        "privacy.location.disabled",
        "Disable Location Access",
        "Enables the machine-wide DisableLocation policy "
        "(SOFTWARE\\Policies\\Microsoft\\Windows\\LocationAndSensors), so Windows and "
        "apps cannot request a location fix. Microsoft documents it in Sensors.admx. "
        "The trade is real and is the reason this is Optional rather than Safe: Find "
        "My Device uses the same capability, so turning location off also turns off "
        "locating this machine. Machine-wide, needs elevation.",
        "privacy", "location", "Optional", 5,
        r"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors",
        "DisableLocation", "1", "0",
        tags=["privacy", "location"],
    ),
    reg(
        "privacy.clipboardSync.disabled",
        "Stop Clipboard Syncing Across Devices",
        "Sets the AllowCrossDeviceClipboard policy "
        "(SOFTWARE\\Policies\\Microsoft\\Windows\\System) to 0 so the clipboard no "
        "longer follows this account to other machines. Copying and pasting on this "
        "machine is untouched. Microsoft documents it in OSPolicy.admx. Machine-wide, "
        "needs elevation.",
        "privacy", "clipboard", "Safe", 5,
        r"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
        "AllowCrossDeviceClipboard", "0", "1",
        tags=["privacy", "clipboard"],
    ),
    reg(
        "privacy.backgroundApps.denied",
        "Stop Apps Running in the Background",
        "Sets the LetAppsRunInBackground policy "
        "(SOFTWARE\\Policies\\Microsoft\\Windows\\AppPrivacy) to 2, which is "
        "'Force deny' — Microsoft's own ordering in AppPrivacy.admx is 0 = user "
        "decides, 1 = force allow, 2 = force deny. Mail, chat and other packaged apps "
        "stop updating until you open them, which is the point and also the cost. "
        "Machine-wide, needs elevation.",
        "privacy", "background-apps", "Optional", 5,
        r"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy",
        "LetAppsRunInBackground", "2", "0",
        tags=["privacy", "background", "performance"],
    ),
    reg(
        "privacy.silentAppInstalls.disabled",
        "Stop Silent App Installs",
        "Clears ContentDeliveryManager\\SilentInstalledAppsEnabled so Windows stops "
        "installing suggested apps without asking. Apps already installed are left "
        "alone; this only stops new ones arriving unannounced.",
        "privacy", "ads", "Recommended", 4,
        CDM, "SilentInstalledAppsEnabled", "0", "1",
        tags=["privacy", "ads", "startup"],
    ),
    reg(
        "privacy.thirdPartySuggestions.disabled",
        "Stop Third-Party App Recommendations",
        "Enables the DisableThirdPartySuggestions policy "
        "(SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent), so Windows stops "
        "promoting third-party applications in Start and on the lock screen. Microsoft "
        "documents it in CloudContent.admx. Per-user.",
        "privacy", "ads", "Recommended", 5,
        CLOUD_CU, "DisableThirdPartySuggestions", "1", "0",
        tags=["privacy", "ads"],
    ),
]

storage = [
    reg(
        "storage.longPaths.enabled",
        "Enable Long Path Support",
        "Sets SYSTEM\\CurrentControlSet\\Control\\FileSystem\\LongPathsEnabled to 1 so "
        "Win32 pathnames longer than 260 characters are handled instead of rejected "
        "with a path-too-long error. Microsoft documents the value in FileSys.admx and "
        "it has been available since Windows 10 1809. Applications that hardcode "
        "MAX_PATH themselves will still refuse — this removes the operating system's "
        "limit, not theirs.",
        "storage", "paths", "Safe", 5,
        r"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem",
        "LongPathsEnabled", "1", "0",
        tags=["storage", "compatibility", "paths"],
    ),
]

# powercfg subgroup "USB settings", setting "USB selective suspend setting".
# Both GUIDs are read from `powercfg /aliases` and `powercfg /q` on this
# machine; index 0 is the friendly name "Disabled".
USB_SUB = "2a737441-1930-4402-8d77-b2bebba308a3"
USB_SET = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226"

_usb_detect = (
    f"$out = & powercfg /query SCHEME_CURRENT {USB_SUB} {USB_SET}; "
    f"$row = $out | Select-String 'Current AC Power Setting Index:'; "
    f"if (-not $row) {{ throw 'USB selective suspend reported no AC index' }}; "
    f"[Convert]::ToInt64((($row -split '0x')[1]), 16)"
)

gpu_gaming = [
    {
        "id": "gpu.gaming.usbSelectiveSuspend.disabled",
        "name": "USB Selective Suspend - Disabled (Mains)",
        "description": "Sets the active power scheme's USB selective suspend setting "
                       "to Disabled on AC, so USB devices are never asked to sleep "
                       "while the machine is plugged in. It is the usual fix for a "
                       "headset, wheel or controller dropping out mid-session, and it "
                       "costs a little idle power — which is why it applies to mains "
                       "only and leaves the battery row alone. powercfg exposes the "
                       "setting as subgroup 'USB settings' / 'USB selective suspend "
                       "setting', indices 0 = Disabled and 1 = Enabled.",
        "category": "gpu-gaming",
        "subcategory": "usb",
        "risk": "Optional",
        "evidence": 5,
        "tags": ["gaming", "usb", "latency", "power"],
        "method": "PowerShell",
        "targetValue": "0",
        "defaultValue": "1",
        "detect": {
            "command": _usb_detect,
            "extractPattern": r"(\d+)",
            "expectedApplied": "0",
            "expectedDefault": "1",
        },
        "apply": {
            "command": f"powercfg /setacvalueindex SCHEME_CURRENT {USB_SUB} {USB_SET} 0; "
                       f"powercfg /setactive SCHEME_CURRENT"
        },
        "rollback": {
            "command": f"powercfg /setacvalueindex SCHEME_CURRENT {USB_SUB} {USB_SET} 1; "
                       f"powercfg /setactive SCHEME_CURRENT"
        },
        "verify": {
            "command": _usb_detect,
            "extractPattern": r"(\d+)",
            "expectedApplied": "0",
        },
    },
]


def load(path):
    if not os.path.exists(path):
        return []
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


def save(path, data):
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")


def merge(path, additions):
    """Upsert each addition: replace an existing definition in place, otherwise
    append. Re-running rewrites what this script owns and leaves the rest alone,
    so the file can be edited by hand between runs without being clobbered."""
    existing = load(path)
    new = 0
    for a in additions:
        for i, t in enumerate(existing):
            if t["id"] == a["id"]:
                existing[i] = a
                break
        else:
            existing.append(a)
            new += 1
    save(path, existing)
    return new, len(additions) - new


if __name__ == "__main__":
    created = rewritten = 0
    for filename, additions in [
        ("visual-effects.json", visual + [OLED_FIX]),
        ("explorer.json", explorer),
        ("privacy.json", privacy),
        ("storage.json", storage),
        ("gpu-gaming.json", gpu_gaming),
    ]:
        n, r = merge(os.path.join(ROOT, "tweaks", filename), additions)
        created += n
        rewritten += r
    print(f"{created} new definitions, {rewritten} rewritten in place")
