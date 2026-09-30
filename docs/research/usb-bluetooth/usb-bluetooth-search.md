# Windows USB, Bluetooth, and Search/Indexing Optimization Reference

> Comprehensive reference for Windows 10/11 hardware and service optimization.
> Last reviewed: August 2026. Some registry values may vary by Windows build.

---

## 1. USB Optimization

### 1.1 USB Selective Suspend

USB Selective Suspend allows the hub driver to suspend individual USB ports that are not in use, reducing power consumption. This is critical for laptops/tablets but can cause latency or wake issues on desktops.

#### Powercfg GUIDs

The USB Selective Suspend power setting is governed by the **USB settings** subgroup under the **Active power scheme**.

| Setting | GUID (Subgroup) | GUID (Setting) |
|---|---|---|
| USB Selective Suspend | `2a737441-1930-4402-8d77-b2bebba308a3` | `48e6b7a6-50f5-4782-a5d4-53bb8f07e226` |

**Values:** `0` = Disabled, `1` = Enabled (default)

#### Commands

```cmd
:: View current AC value (1=enabled, 0=disabled)
powercfg /query SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226

:: Disable USB Selective Suspend on AC power
powercfg /setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0

:: Disable USB Selective Suspend on DC (battery) power
powercfg /setdcvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0

:: Enable USB Selective Suspend on AC power
powercfg /setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 1

:: Enable USB Selective Suspend on DC (battery) power
powercfg /setdcvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 1

:: Apply changes to active scheme
powercfg /setactive SCHEME_CURRENT
```

To target a specific power plan by GUID:

```cmd
:: List all power plans
powercfg /list

:: Set for a specific plan (replace <PLAN_GUID>)
powercfg /setacvalueindex <PLAN_GUID> 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0
powercfg /setactive <PLAN_GUID>
```

#### Registry - System-Wide Disable

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\USB
Value:  DisableSelectiveSuspend
Type:   DWORD
Data:   1 = Disabled (Selective Suspend off for all USB hubs)
        0 = Enabled (default, Selective Suspend active)
```

```cmd
:: Disable via registry (requires admin + reboot)
reg add "HKLM\SYSTEM\CurrentControlSet\Services\USB" /v DisableSelectiveSuspend /t REG_DWORD /d 1 /f

:: Re-enable via registry
reg add "HKLM\SYSTEM\CurrentControlSet\Services\USB" /v DisableSelectiveSuspend /t REG_DWORD /d 0 /f
```

#### GUI Method

1. Open Device Manager (`devmgmt.msc`)
2. Expand **Universal Serial Bus controllers**
3. Right-click **USB Root Hub** (or **USB Root Hub (USB 3.0)**) > Properties
4. Go to **Power Management** tab
5. Uncheck **Allow the computer to turn off this device to save power**
6. Repeat for every USB Root Hub entry

#### Group Policy

```
Computer Configuration > Administrative Templates > System > Power Management > USB Settings
  - USB selective suspend setting: Disabled
```

---

### 1.2 USB Hub Power Management

Each USB host controller and root hub has independent power management settings.

#### Per-Hub Registry Keys

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\USBHub\Parameters
  or:   HKLM\SYSTEM\CurrentControlSet\Services\USBHUB3\Parameters  (for USB 3.x hubs)

Value:  AllowIdleIrpInD3
Type:   DWORD
Data:   0 = Do not allow idle IRPs in D3 (better performance, more power)
        1 = Allow idle IRPs in D3 (default, more power saving)
```

#### EnhancedPowerManagementEnabled (Per-Device)

Each USB device enumerated under the USB device class can have this override:

```
Key:    HKLM\SYSTEM\CurrentControlSet\Enum\USB\{VID_PID_serial}\{instance}\Device Parameters
Value:  EnhancedPowerManagementEnabled
Type:   DWORD
Data:   0 = Disabled for this device
        1 = Enabled for this device (default)
```

To find your device instance IDs:

```cmd
wmic path Win32_PnPEntity where "PNPClass='USB'" get DeviceID,Name /format:list
```

#### USB Power Budget Considerations

- USB 2.0 port: 500mA (high-power) or 100mA (low-power)
- USB 3.x port: 900mA
- USB-C with Power Delivery: up to 240W (USB PD 3.1 EPR)
- A single USB 3.0 root hub controller typically has a 127-device budget
- Powered hubs draw from the bus but supply their own downstream power
- Underpowered USB devices (external HDDs, webcams, RGB peripherals) can cause enumeration failures or disconnects

---

### 1.3 USB Polling Rate

The USB polling rate determines how frequently the host queries a HID device (mouse, keyboard) for new input data.

#### Default Polling Rates

| USB Version | Default Interval | Default Rate | Max Rate |
|---|---|---|---|
| USB 1.1 (Low Speed) | 10ms | 100Hz | 100Hz |
| USB 1.1 (Full Speed) | 1ms | 1000Hz | 1000Hz |
| USB 2.0 (Hi-Speed) | 1ms (for HID) | 1000Hz | 1000Hz |
| USB 3.x | 125us (for HID) | 8000Hz | 8000Hz (theoretical, rarely used) |

**Note:** USB 2.0 High Speed HID devices typically report at 1ms intervals (1000Hz) by default. However, some devices enumerate at 8ms (125Hz) depending on the descriptor and driver behavior. The Windows default HID driver uses the bInterval value from the endpoint descriptor.

#### Registry - Mouse Data Queue Size

Controls the number of mouse data packets buffered in the HID driver before being consumed.

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\mouhid\Parameters
Value:  MouseDataQueueSize
Type:   DWORD
Data:   16 (default, range: 0x1 to 0x64, i.e., 1 to 100)
```

Higher values buffer more input events, which can smooth input at high polling rates but increase latency.

```cmd
:: Set mouse data queue size to 32
reg add "HKLM\SYSTEM\CurrentControlSet\Services\mouhid\Parameters" /v MouseDataQueueSize /t REG_DWORD /d 32 /f

:: Set to minimum (lowest latency)
reg add "HKLM\SYSTEM\CurrentControlSet\Services\mouhid\Parameters" /v MouseDataQueueSize /t REG_DWORD /d 1 /f
```

#### Registry - Keyboard Data Queue Size

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\kbdhid\Parameters
Value:  KeyboardDataQueueSize
Type:   DWORD
Data:   100 (default, range: 0x1 to 0x64)
```

```cmd
:: Set keyboard data queue size
reg add "HKLM\SYSTEM\CurrentControlSet\Services\kbdhid\Parameters" /v KeyboardDataQueueSize /t REG_DWORD /d 1 /f
```

#### How to Change Actual Polling Rate

The polling rate is fundamentally set by the **device firmware** and the **endpoint descriptor** in the USB descriptor. Windows respects the `bInterval` value declared by the device.

**Methods to change polling rate:**

1. **Device software/driver** -- Many gaming mouse/keyboard manufacturers (Logitech, Razer, Corsair, SteelSeries) include software that can change the device polling rate:
   - Logitech G HUB: Mouse Settings > Report Rate
   - Razer Synapse: Performance > Polling Rate
   - Corsair iCUE: Device Settings > Polling Rate
   - These write the setting to onboard firmware; it persists without the software running.

2. **HIDUSB lower filter driver** -- You can write a custom filter driver that overrides bInterval, but this is complex and rarely done.

3. **Device Manager advanced properties** -- Some HID devices expose polling rate in Device Manager:
   - Device Manager > HID Device > Properties > Details > Hardware Ids
   - Some devices have custom properties tab

4. **USB device tree inspection** -- To check actual polling rate:
   ```cmd
   :: Using USBTree or USBView (from Windows SDK)
   :: Or via PowerShell to query device properties:
   powershell -Command "Get-PnpDeviceProperty -InstanceId '<device-instance-id>' -KeyName 'DEVPKEY_Device_BusReportedDeviceDesc' | Select-Object Data"
   ```

5. **Third-party tools:**
   - **Mouse Rate Checker** -- measures actual polling rate
   - **USBTreeView** (by NirSoft) -- shows actual endpoint intervals
   - **USBDeview** (by NirSoft) -- lists USB device properties including polling

#### Performance vs Battery Impact

| Polling Rate | Latency | CPU Usage | Battery Impact |
|---|---|---|---|
| 125Hz (8ms) | 8ms | Low | Minimal |
| 250Hz (4ms) | 4ms | Low-Medium | Low |
| 500Hz (2ms) | 2ms | Medium | Moderate |
| 1000Hz (1ms) | 1ms | Medium-High | Noticeable |
| 2000Hz+ | <1ms | High | Significant |

- For **desktops/gaming**: 1000Hz is recommended for competitive gaming; 500Hz is a good balance.
- For **laptops on battery**: 125-250Hz is ideal. Higher rates consume more power and generate more interrupts.
- Every USB interrupt wakes the CPU from deeper C-states, reducing overall system power efficiency.

---

### 1.4 USB Power Registry Settings

#### USB Device Class Registry Key

```
Key:    HKLM\SYSTEM\CurrentControlSet\Control\Class\{36fc9e60-c465-11cf-8056-444553540000}
```

This is the USB device class GUID. Individual device instances are listed as subkeys (e.g., `0000`, `0001`, etc.).

**Common values under each device instance:**

```
Value:  EnhancedPowerManagementEnabled
Type:   DWORD
Data:   0 = Disable enhanced power management for this device
        1 = Enable (default)
```

```
Value:  AllowIdleIrpInD3
Type:   DWORD
Data:   0 = Don't allow idle IRPs while device is in D3 power state
        1 = Allow (default)
```

#### USB 3.x / 3.1 Link Power Management (U1/U2 States)

USB 3.x introduces link-level power states:

- **U0**: Active (fully operational)
- **U1**: Idle -- low latency wakeup (~10us), link enters low power
- **U2**: Suspended -- higher latency wakeup (~2ms), deeper power savings
- **U3**: Fully suspended -- same as selective suspend

These are controlled per host controller:

```
Key:    HKLM\SYSTEM\CurrentControlSet\Enum\PCI\{device-id}\{instance-id}\Device Parameters
  (where the PCI device is the USB 3.x host controller, e.g., xHCI)
```

Powercfg also has a related setting:

```cmd
:: USB 3.0 Link State Power Management (powercfg GUIDs)
:: Subgroup: 2a737441-1930-4402-8d77-b2bebba308a3 (USB settings)
:: Setting:  d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d

:: Values:
::   0 = Disabled (always U0, max performance)
::   1 = Moderate power savings (U1 enabled)
::   2 = Maximum power savings (U2 enabled)

:: Disable USB 3.0 link power management on AC:
powercfg /setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d 0

:: Disable USB 3.0 link power management on battery:
powercfg /setdcvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d 0

powercfg /setactive SCHEME_CURRENT
```

#### USB Selective Suspend Per-Device (via Device Manager)

Each USB device instance can override selective suspend:

```
Key:    HKLM\SYSTEM\CurrentControlSet\Enum\USB\{VID_PID}\{instance}\Device Parameters\WDF
Value:  SystemWakeIgnore
Type:   DWORD
Data:   1 = Device can wake the system from selective suspend
```

---

## 2. Bluetooth Optimization

### 2.1 Bluetooth Adapter Power Management

#### Powercfg Settings

Bluetooth has its own powercfg subgroup:

| Setting | Subgroup GUID | Setting GUID |
|---|---|---|
| Bluetooth | `9596fb26-9850-41fd-ac3e-f7c3c00afd4b` | `12bbebe6-58d6-4636-95bb-3217ef867c1a` |

```cmd
:: Query Bluetooth adapter settings
powercfg /query SCHEME_CURRENT 9596fb26-9850-41fd-ac3e-f7c3c00afd4b

:: Disable Bluetooth adapter power saving on AC
powercfg /setacvalueindex SCHEME_CURRENT 9596fb26-9850-41fd-ac3e-f7c3c00afd4b 12bbebe6-58d6-4636-95bb-3217ef867c1a 0

:: Re-enable
powercfg /setacvalueindex SCHEME_CURRENT 9596fb26-9850-41fd-ac3e-f7c3c00afd4b 12bbebe6-58d6-4636-95bb-3217ef867c1a 1

powercfg /setactive SCHEME_CURRENT
```

#### Device Manager Power Management

1. Open Device Manager (`devmgmt.msc`)
2. Expand **Bluetooth**
3. Right-click your Bluetooth adapter > Properties
4. **Power Management** tab:
   - Uncheck **Allow the computer to turn off this device to save power**
   - This prevents the adapter from entering low-power states

#### Registry - BTHPORT Power Management

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\PowerManagement
Value:  AutoPowerDownEnabled
Type:   DWORD
Data:   0 = Disable auto power-down of Bluetooth radio
        1 = Enable auto power-down (default)
```

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\PowerManagement
Value:  ControllerPowerDownIdleTimeoutMs
Type:   DWORD
Data:   Timeout in milliseconds before the controller powers down when idle
        Default: 30000 (30 seconds)
        Set to 0xFFFFFFFF to never power down
```

#### Bluetooth Adapter Registry Keys

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\BTHUSB\Parameters
Value:  DisableRadioPowerDown
Type:   DWORD
Data:   1 = Prevent Bluetooth radio from powering down
        0 = Allow power management (default)
```

---

### 2.2 Bluetooth Audio Quality vs Latency

#### Profile Comparison

| Profile | Quality | Latency | Bandwidth | Use Case |
|---|---|---|---|---|
| A2DP (Stereo) | High | ~100-200ms | Up to 1 Mbps | Music, media |
| HFP (Hands-Free) | Low (8kHz/16kHz) | ~100-150ms | ~64 kbps | Calls, voice |
| LE Audio (LC3) | Variable | <50ms | Variable | New BT 5.2+ |
| HID | N/A | ~5-10ms | Low | Mouse, keyboard |

#### Codec Comparison

| Codec | Bit Rate | Sample Rate | Latency | Quality | Windows Native |
|---|---|---|---|---|---|
| SBC | 328 kbps | 48 kHz | ~150ms | Good | Yes |
| AAC | 256 kbps | 44.1 kHz | ~150ms | Good | Yes (Win 10+) |
| aptX | 384 kbps | 48 kHz | ~70ms | Good | No (needs driver) |
| aptX HD | 576 kbps | 48 kHz | ~100ms | Very Good | No (needs driver) |
| aptX Low Latency | 420 kbps | 48 kHz | ~40ms | Good | No (needs driver) |
| LDAC | 990 kbps | 96 kHz | ~200ms | Excellent | No (needs driver) |
| LC3 | 345 kbps | 48 kHz | ~20-30ms | Excellent | Win 11 22H2+ |
| LC3plus | 562 kbps | 96 kHz | ~15ms | Excellent | BT 5.4+ |

**Key points:**
- **SBC** is the mandatory baseline codec; all A2DP devices support it. Decent quality but higher latency.
- **AAC** is well-supported on Windows 10 1903+ and Windows 11. Good for Apple AirPods.
- **aptX/aptX HD** require a Qualcomm/CSR Bluetooth adapter. Intel adapters do not support aptX natively.
- **LDAC** requires a Sony-compatible adapter or third-party driver patching.
- **LC3** is the standard codec for Bluetooth LE Audio (Bluetooth 5.2+). Windows 11 22H2+ includes native LC3 support with compatible hardware.

#### How to Check/Change Bluetooth Codec on Windows

**Windows does not expose a GUI codec selector.** The codec negotiation happens automatically between the adapter and the headphones.

**Methods to influence codec selection:**

1. **Device Manager codec properties** (Windows 11):
   - Device Manager > Bluetooth > your adapter > Properties > Details
   - Look for `BTH enumeration` or specific codec properties
   - Some adapters expose codec preference under the audio endpoint properties

2. **Bluetooth Explorer** (Apple-specific, for AirPods on Windows):
   - The AAC codec is used by default with AirPods if the adapter supports it

3. **Manufacturer software:**
   - Intel Wireless Bluetooth: Driver installs include an "Intel Bluetooth Configuration" utility that may offer codec preferences
   - Qualcomm aptX adapters may include a configuration panel

4. **Third-party tools:**
   - **Bluetooth LE Explorer** (Microsoft Store) -- inspect BT GATT services and codec capabilities
   - Some adapters support configuration via `btconfig.exe` or manufacturer utilities

#### Switching Between A2DP and HFP

When a Bluetooth headset is connected, Windows creates two audio endpoints:
- **Stereo (A2DP)** -- high quality, no microphone
- **Mono/Hands-Free (HFP)** -- lower quality, with microphone

Windows automatically selects the profile based on the application:
- Media players use A2DP by default
- Video calls (Teams, Zoom) switch to HFP to use the microphone
- You can manually switch: **Settings > System > Sound > Output** and select the Stereo or Hands-Free endpoint

**To force A2DP for all audio (no microphone):**
- Right-click the speaker icon > Sound settings > Output > select the "Stereo" endpoint

**To force HFP (with microphone):**
- Sound settings > Output > select the "Hands-Free" or "Mono" endpoint

---

### 2.3 Bluetooth Power Saving vs Performance

#### Link Supervision Timeout

The Link Supervision Timeout (LST) determines how long the controller waits before declaring a connection lost.

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters
Value:  LinkSupervisionTimeout
Type:   DWORD
Data:   Timeout in slots (1 slot = 0.625ms)
        Default: 8000 (5 seconds)
        Range: 0x0006 to 0x0C80 (3.75ms to 5 seconds)
        Recommended for audio: 8000-16000
        Recommended for peripherals: 20000-32000
```

Lower values = faster disconnection detection but more sensitive to interference.
Higher values = more tolerance for interference but slower loss detection.

#### Sniff Mode Settings

Sniff mode allows the Bluetooth controller to reduce power by only listening at defined intervals.

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\PowerManagement
Value:  SniffModeEnabled
Type:   DWORD
Data:   1 = Allow sniff mode (default, power saving)
        0 = Disable sniff mode (always active, more power)

Value:  SniffIntervalMin
Type:   DWORD
Data:   Minimum sniff interval in slots (0.625ms each)
        Default: 8 (5ms)
        Range: 6-3200

Value:  SniffIntervalMax
Type:   DWORD
Data:   Maximum sniff interval in slots
        Default: 8 (5ms)
        Range: 6-3200
```

**For high-performance (gaming peripherals, audio):**
- Disable sniff mode or reduce intervals
- Smaller intervals = lower latency but higher power consumption

#### Connection Interval (BLE)

For Bluetooth Low Energy devices, the connection interval determines how often the central and peripheral exchange packets.

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters
Value:  PreferredConnectionIntervalMin
Type:   DWORD
Data:   In 1.25ms units
        Default: 6 (7.5ms) -- minimum allowed by BLE spec
        Range: 6-3200 (7.5ms to 4 seconds)

Value:  PreferredConnectionIntervalMax
Type:   DWORD
Data:   Default: 32 (40ms)
        Range: 6-3200
```

**For gaming mice/keyboards (BLE):** Set to `6` (7.5ms) for both min and max.
**For sensors/IoT:** Wider intervals (100-500ms) save battery.

---

### 2.4 Bluetooth Registry Keys Reference

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\BTHUSB\Parameters
```

| Value | Type | Description | Default |
|---|---|---|---|
| `DisableRadioPowerDown` | DWORD | Prevent radio power-down | 0 |
| `NumAclMemoryBlocks` | DWORD | ACL buffer blocks | 50 |
| `NumCompletedPktsPoolSize` | DWORD | Completed packet pool size | 200 |

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters
```

| Value | Type | Description | Default |
|---|---|---|---|
| `LinkSupervisionTimeout` | DWORD | LST in slots | 8000 |
| `PageScanRepetitionMode` | DWORD | Page scan rep mode (1, 2, or 3) | 2 |
| `MinimumEncryptionKeySize` | DWORD | Min encryption key size | 7 |
| `PreferredConnectionIntervalMin` | DWORD | BLE conn interval min (x1.25ms) | 6 |
| `PreferredConnectionIntervalMax` | DWORD | BLE conn interval max (x1.25ms) | 32 |

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\PowerManagement
```

| Value | Type | Description | Default |
|---|---|---|---|
| `AutoPowerDownEnabled` | DWORD | Auto power-down radio | 1 |
| `ControllerPowerDownIdleTimeoutMs` | DWORD | Idle timeout before power-down (ms) | 30000 |
| `SniffModeEnabled` | DWORD | Allow sniff mode | 1 |
| `SniffIntervalMin` | DWORD | Min sniff interval (slots) | 8 |
| `SniffIntervalMax` | DWORD | Max sniff interval (slots) | 8 |

---

## 3. Windows Search & Indexing Optimization

### 3.1 WSearch Service

The Windows Search service (`WSearch`) maintains an index of file contents, properties, and Start Menu entries to provide fast search results.

#### Service Management Commands

```cmd
:: Check WSearch status
sc query WSearch

:: Stop WSearch
sc stop WSearch

:: Start WSearch
sc start WSearch

:: Disable WSearch (boot-level)
sc config WSearch start= disabled

:: Set to manual start
sc config WSearch start= demand

:: Set to automatic (default)
sc config WSearch start= auto

:: Set to delayed start
sc config WSearch start= delayed-auto

:: Delete WSearch entirely (not recommended)
sc delete WSearch
```

**PowerShell equivalents:**

```powershell
# Check status
Get-Service WSearch

# Stop and disable
Stop-Service WSearch -Force
Set-Service WSearch -StartupType Disabled

# Enable and start
Set-Service WSearch -StartupType Automatic
Start-Service WSearch

# Set to delayed start
Set-Service WSearch -StartupType Automatic -StartupTypeDelay 30
```

#### WSearch Registry Keys

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\WSearch
Value:  Start
Type:   DWORD
Data:   0 = Boot
        1 = System
        2 = Automatic (default)
        3 = Manual
        4 = Disabled

Value:  DisableDynamicBackoff
Type:   DWORD
Data:   0 = Allow dynamic backoff (default)
        1 = Disable dynamic backoff (constant indexing rate, uses more CPU)
```

```
Key:    HKLM\SYSTEM\CurrentControlSet\Services\WSearch\Config
Value:  MaxIndexSize
Type:   DWORD
Data:   Maximum index size in bytes (default varies by system)
        Set to limit index growth

Value:  PerUserIndexingScope
Type:   DWORD
Data:   1 = Per-user index (default in modern Windows)
        0 = System-wide index (legacy)
```

---

### 3.2 Index Location and Management

#### Default Index Location

```
C:\ProgramData\Microsoft\Search\Data\Applications\Windows\Windows.edb
```

Per-user index (Windows 10 1903+):
```
%LocalAppData%\Microsoft\Search\Data\Applications\Windows\Windows.edb
```

Previous Windows versions also used:
```
C:\ProgramData\Microsoft\Search\Data\Applications\Windows\Windows.edb
C:\ProgramData\Microsoft\Search\Data\Documents\Windows.edb
C:\ProgramData\Microsoft\Search\Data\Plugins\Windows\...
```

#### How to Move the Index Location

**Method 1: Windows Search Settings GUI**
1. Open **Control Panel > Indexing Options**
2. Click **Advanced**
3. Under **Index Settings** tab, click **Select New** next to "Index location"
4. Browse to new location (e.g., `D:\SearchIndex`)
5. Click **OK** and rebuild the index

**Method 2: Registry**

```
Key:    HKLM\SOFTWARE\Microsoft\Windows Search
Value:  DataDirectory
Type:   REG_EXPAND_SZ
Data:   %ProgramData%\Microsoft\Search\Data\Applications\Windows
        Change to your desired path, e.g.: D:\SearchIndex\Data
```

**Method 3: Symbolic link / Junction (advanced)**

```cmd
:: Stop WSearch first
sc stop WSearch

:: Move index to new drive
robocopy "C:\ProgramData\Microsoft\Search" "D:\SearchIndex" /E /MOVE

:: Create symbolic link
mklink /J "C:\ProgramData\Microsoft\Search" "D:\SearchIndex"

:: Restart WSearch
sc start WSearch
```

#### Index Rebuild

```cmd
:: Rebuild via command line (requires stopping WSearch)
sc stop WSearch
timeout /t 5
del "C:\ProgramData\Microsoft\Search\Data\Applications\Windows\Windows.edb"
sc start WSearch
```

**GUI rebuild:**
1. **Control Panel > Indexing Options > Advanced > Rebuild**

**PowerShell rebuild:**

```powershell
# Stop service, delete index, restart
Stop-Service WSearch -Force
Start-Sleep -Seconds 5
$edbPath = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Search" -Name DataDirectory -ErrorAction SilentlyContinue).DataDirectory
if (-not $edbPath) { $edbPath = "$env:ProgramData\Microsoft\Search\Data\Applications\Windows" }
Remove-Item "$edbPath\Windows.edb" -Force -ErrorAction SilentlyContinue
Start-Service WSearch
```

#### Index Size Management

```cmd
:: Check current index size
dir "C:\ProgramData\Microsoft\Search\Data\Applications\Windows\Windows.edb"

:: Typical sizes:
:: Small system (few files): 50-200 MB
:: Medium system: 200-800 MB
:: Large system (dev machine): 1-5 GB
:: Very large: 5-20 GB (unusual, indicates bloat)
```

To reduce index size:
- Remove indexed locations in Control Panel > Indexing Options
- Exclude file types that don't need indexing
- Clear the index and rebuild

---

### 3.3 What Gets Indexed

By default, Windows indexes:

| Category | Location | Includes |
|---|---|---|
| Start Menu | `%AppData%\Microsoft\Windows\Start Menu` | Program shortcuts, names |
| Start Menu (all users) | `%ProgramData%\Microsoft\Windows\Start Menu` | Program shortcuts |
| User Profile | `%USERPROFILE%` | Documents, Desktop, etc. |
| Outlook email | Local .ost/.pst files | Subject, body, attachments |
| Windows libraries | Documents, Pictures, Music, Videos | File names and contents |
| Offline files | `%SystemRoot%\CSC` | Cached network files |
| Internet Explorer history | `%LOCALAPPDATA%\Microsoft\Windows\History` | Browsing history |
| Windows Shell | System paths | Executables, DLLs (metadata only) |

#### What is NOT indexed by default:
- Program Files (metadata only, no file contents)
- Windows directory (metadata only)
- System32 (metadata only)
- Temporary files, recycle bin

---

### 3.4 Indexing Performance Impact

#### Disk I/O During Indexing

- Full initial index: High disk I/O for 5-30 minutes depending on data volume
- Incremental updates: Minimal I/O (only changed files)
- On **SSD**: Negligible impact; indexing is fast and barely noticeable
- On **HDD**: Can cause significant slowdowns during initial/rebuild indexing

#### CPU Usage (SearchIndexer.exe)

```cmd
:: Check SearchIndexer.exe CPU/memory usage
tasklist /fi "imagename eq SearchIndexer.exe"
tasklist /fi "imagename eq SearchIndexer.exe" /v

:: Monitor in real-time
powershell -Command "Get-Process SearchIndexer | Select-Object CPU,WorkingSet64,Id"
```

- During initial indexing: can use 5-25% CPU continuously
- During steady-state: minimal CPU (0-1%)
- Dynamic backoff feature (default) automatically reduces indexing rate when system is under load
- Disabling dynamic backoff (`DisableDynamicBackoff = 1`) forces constant indexing rate

#### Memory Usage

- SearchIndexer.exe typically uses 100-500 MB RAM
- Can grow to 1-2 GB on large indices
- Most memory is used for the index cache and search queries

#### How to Limit Indexing to Specific Locations

**GUI:**
1. Control Panel > Indexing Options
2. Click **Modify** to add/remove indexed locations
3. Uncheck entire drives or specific folders

**Registry (to add exclusions programmatically):**

```
Key:    HKLM\SOFTWARE\Microsoft\Windows Search\CrawlScopeManager
  (Index entries are stored here as registry values)
```

Better approach -- add via Group Policy:
```
Computer Configuration > Administrative Templates > Windows Components > Search
  - Do not allow locations on removable drives to be added to libraries
  - Default excluded paths
```

#### Exclusion Patterns

In the **Indexing Options > Advanced > File Types** tab:
- Uncheck specific extensions to exclude them entirely
- Choose between "Index Properties Only" and "Index Properties and File Contents"

PowerShell to manage indexed extensions:

```powershell
# List all file extensions and their indexing status
$exts = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Search\CrawlScopeManager\Extensions"
# (structure varies by Windows build)
```

---

### 3.5 Registry Settings for Search Optimization

#### HKLM\SOFTWARE\Microsoft\Windows Search

```
Key:    HKLM\SOFTWARE\Microsoft\Windows Search
```

| Value | Type | Description | Default |
|---|---|---|---|
| `SetupCompletedSuccessfully` | DWORD | 1 = Initial setup done | 1 |
| `DisabledByDefault` | DWORD | 1 = Search service disabled by default | 0 |
| `DataDirectory` | REG_EXPAND_SZ | Index data location | `%ProgramData%\...\Windows` |
| `EnableIndexingSpeedup` | DWORD | 1 = Faster indexing (more CPU) | 1 |
| `PrePopulateCacheOnly` | DWORD | 1 = Only use cache for suggestions | 0 |
| `ConnectedSearchUseWeb` | DWORD | 1 = Include web results in search | 1 |
| `AllowCloudSearch` | DWORD | 1 = Allow cloud search | 1 |
| `BingSearchEnabled` | DWORD | 1 = Bing results in Start search | 1 |
| `CortanaConsent` | DWORD | 1 = Cortana consented | varies |
| `SearchBoxVersion` | DWORD | Search box UI version | varies |

```
Key:    HKLM\SOFTWARE\Microsoft\Windows Search\VolumeCacheCategories
  (Defines what categories to include in volume cache indexing)
```

#### HKCU Search Settings

```
Key:    HKCU\Software\Microsoft\Windows\CurrentVersion\Search
```

| Value | Type | Description | Default |
|---|---|---|---|
| `BingSearchEnabled` | DWORD | 1 = Bing results in search | 1 |
| `CortanaConsent` | DWORD | 1 = Cortana/online search enabled | 1 |
| `SearchboxTaskbarMode` | DWORD | 0 = Hide, 1 = Search icon, 2 = Search box | 2 |
| `DeviceHistoryEnabled` | DWORD | 1 = Device history | 0 |
| `HistoryViewEnabled` | DWORD | 1 = Search history | 1 |

```cmd
:: Disable Bing/web results in Windows Search
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Search" /v BingSearchEnabled /t REG_DWORD /d 0 /f

:: Disable web results via Group Policy
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search" /v DisableWebSearch /t REG_DWORD /d 1 /f

:: Disable search suggestions
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search" /v ConnectedSearchUseWeb /t REG_DWORD /d 0 /f

:: Disable cloud content search
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search" /v AllowCloudSearch /t REG_DWORD /d 0 /f
```

#### Additional Policy Keys

```
Key:    HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search
```

| Value | Type | Description |
|---|---|---|
| `DisableWebSearch` | DWORD | 1 = Disable web search in Start |
| `ConnectedSearchUseWeb` | DWORD | 0 = No web in search |
| `AllowCloudSearch` | DWORD | 0 = No cloud search |
| `AllowCortana` | DWORD | 0 = Disable Cortana |
| `DisableIndexerOutlook` | DWORD | 1 = Don't index Outlook email |
| `PreventRemoteQuery` | DWORD | 1 = Block remote queries |
| `AllowSearchToUseLocation` | DWORD | 0 = Don't use location for search |
| `EnableSemanticZoom` | DWORD | 0 = Disable semantic zoom in search |
| `AlwaysShowNetworkResults` | DWORD | 0 = Hide network results |
| `ConnectedSearchDefaultMode` | DWORD | 0 = Restricted mode |
| `ConnectedSearchSafeSearchMode` | DWORD | 0 = Strict, 1 = Moderate, 2 = Off |

---

## Quick Reference: Combined Optimization Script

Below is a combined batch/cmd script skeleton for applying key optimizations:

```cmd
@echo off
:: ============================================================
:: Windows USB + Bluetooth + Search Optimization Script
:: Run as Administrator
:: ============================================================

echo ==========================================
echo  USB Optimization
echo ==========================================

:: Disable USB Selective Suspend (AC)
powercfg /setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0
:: Disable USB 3.0 Link Power Management (AC)
powercfg /setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d 0
:: Minimize mouse input buffer for lower latency
reg add "HKLM\SYSTEM\CurrentControlSet\Services\mouhid\Parameters" /v MouseDataQueueSize /t REG_DWORD /d 1 /f
:: Minimize keyboard input buffer
reg add "HKLM\SYSTEM\CurrentControlSet\Services\kbdhid\Parameters" /v KeyboardDataQueueSize /t REG_DWORD /d 1 /f

echo ==========================================
echo  Bluetooth Optimization
echo ==========================================

:: Disable BT radio auto power-down
reg add "HKLM\SYSTEM\CurrentControlSet\Services\BTHUSB\Parameters" /v DisableRadioPowerDown /t REG_DWORD /d 1 /f
:: Disable BT adapter power saving
powercfg /setacvalueindex SCHEME_CURRENT 9596fb26-9850-41fd-ac3e-f7c3c00afd4b 12bbebe6-58d6-4636-95bb-3217ef867c1a 0

echo ==========================================
echo  Windows Search Optimization
echo ==========================================

:: Disable Bing/web results in Start
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Search" /v BingSearchEnabled /t REG_DWORD /d 0 /f
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search" /v DisableWebSearch /t REG_DWORD /d 1 /f
:: Disable cloud search
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search" /v AllowCloudSearch /t REG_DWORD /d 0 /f

:: Apply all powercfg changes
powercfg /setactive SCHEME_CURRENT

echo.
echo Done. Some changes require a reboot to take effect.
pause
```

---

> **Disclaimer:** Registry modifications can affect system stability. Always create a System Restore point before making changes. Test on non-production systems first. Values and availability may differ between Windows builds (21H2, 22H2, 23H2, 24H2, etc.).
