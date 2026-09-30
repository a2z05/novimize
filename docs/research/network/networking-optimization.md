# Windows Networking Optimization

A comprehensive reference for tuning the Windows TCP/IP stack, NIC properties, DNS, Wi-Fi, and
related subsystems for lower latency, higher throughput, and reduced overhead.

---

## Table of Contents

1. [TCP/IP Stack Optimization](#1-tcpip-stack-optimization)
2. [Nagle's Algorithm](#2-nagles-algorithm)
3. [DNS Optimization](#3-dns-optimization)
4. [NIC Advanced Properties](#4-nic-advanced-properties)
5. [Network Throttling Index](#5-network-throttling-index)
6. [Delivery Optimization](#6-delivery-optimization)
7. [Wi-Fi Optimization](#7-wi-fi-optimization)
8. [Netsh Commands Reference](#8-netsh-commands-reference)
9. [MTU Optimization](#9-mtu-optimization)
10. [Revert / Rollback](#10-revert--rollback)

---

## 1. TCP/IP Stack Optimization

Windows ships a set of tunable TCP parameters via `netsh`. The defaults target a balance between
throughput and latency for general use. The settings below shift that balance toward lower latency
(gaming, real-time media) or higher throughput (large file transfers).

### 1.1 View Current TCP Global Settings

```powershell
# Show all TCP global parameters
netsh int tcp show global

# Show supplemental (per-interface) TCP settings
netsh int tcp show supplemental
```

Typical output of `netsh int tcp show global`:

```
Query Setting State
--------------------------------------------------
Add-On-State              enabled
Chimney Offload State      disabled
Congestion Control Provider default
Dca                       enabled
ECN Capability             disabled
RFC 1323 Timestamps       disabled
Initial RTO (ms)          3000
Receive Window Auto-Tuning Level normal
Receive-Side Scaling State enabled
RSC                       enabled
Sack                      enabled
Timestamps                disabled
```

### 1.2 TCP Auto-Tuning (Receive Window)

Auto-Tuning dynamically adjusts the TCP receive window size to optimize throughput per connection.

| Level              | Description                                                            |
|--------------------|------------------------------------------------------------------------|
| `normal`           | Default. Window scales up aggressively for bulk transfers.             |
| `disabled`         | Fixed window. Only for troubleshooting or if apps break with scaling.  |
| `restricted`       | Scales up slowly. Good baseline for mixed workloads.                   |
| `highlyrestricted` | Very conservative scaling. Diagnostic use only.                        |

```powershell
# Set auto-tuning to normal (default)
netsh int tcp set global autotuninglevel=normal

# Set auto-tuning to restricted (lower latency, less buffering)
netsh int tcp set global autotuninglevel=restricted

# Set auto-tuning to highly restricted (minimum buffering)
netsh int tcp set global autotuninglevel=highlyrestricted

# Disable auto-tuning entirely (diagnostic only)
netsh int tcp set global autotuninglevel=disabled
```

**Recommendation:** Use `normal` for general use. Use `restricted` on gaming or real-time systems
where latency matters more than throughput.

### 1.3 Receive-Side Scaling (RSS)

RSS distributes incoming network processing across multiple CPU cores. Almost always beneficial on
multi-core systems.

```powershell
# Enable RSS (should already be enabled by default)
netsh int tcp set global rss=enabled

# Disable RSS (diagnostic only, or single-core systems)
netsh int tcp set global rss=disabled
```

Per-NIC RSS settings are also available in Advanced NIC properties (see Section 4).

### 1.4 Chimney Offload

Chimney Offload was designed to offload TCP processing to the NIC hardware. It is deprecated since
Windows Server 2012 and Windows 8 and can cause connectivity issues. Always disable it.

```powershell
# Disable chimney offload (deprecated, should already be off)
netsh int tcp set global chimney=disabled
```

### 1.5 TCP Timestamps

TCP Timestamps (RFC 1323) add an 8-byte header option to every packet for RTT measurement and
PAWS (Protection Against Wrapped Sequences). Disabling saves 12 bytes per packet (8 bytes option
+ 4 bytes padding to align) on small payloads and reduces CPU work, but can affect high-speed
long-distance links.

```powershell
# Disable TCP timestamps
netsh int tcp set global timestamps=disabled

# Enable TCP timestamps (default)
netsh int tcp set global timestamps=enabled
```

**Recommendation:** Disable for gaming and low-latency scenarios. Keep enabled on WAN links
longer than ~1 Gbps round-trip where PAWS is beneficial.

### 1.6 Initial Retransmission Timeout (RTO)

The initial RTO controls how long TCP waits before retransmitting an unacknowledged segment.
Lower values detect packet loss faster but may cause unnecessary retransmissions on lossy links.

```powershell
# Set initial RTO to 3000ms (default)
netsh int tcp set global initialRto=3000

# Minimum allowed value: 2000ms
netsh int tcp set global initialRto=2000

# Maximum allowed value: 60000ms
netsh int tcp set global initialRto=60000
```

**Recommendation:** Leave at default (3000ms) unless you have a very stable, low-latency network
where 2000ms is safe.

### 1.7 Non-SACK RTT Resiliency

When enabled, Windows uses RTT estimation even when SACK (Selective ACK) is negotiated, which
provides more stable RTT measurements under loss.

```powershell
# Disable non-SACK RTT resiliency (default on some versions)
netsh int tcp set global nonsackrttresiliency=disabled

# Enable non-SACK RTT resiliency
netsh int tcp set global nonsackrttresiliency=enabled
```

**Recommendation:** Keep disabled unless experiencing erratic RTT measurements.

### 1.8 Maximum SYN Retransmissions

Controls how many times TCP will retransmit a SYN (connection request) before giving up.
Lower values fail faster on unreachable hosts.

```powershell
# Set max SYN retransmissions (default: 2)
netsh int tcp set global maxsynretransmissions=2

# More aggressive (fail faster)
netsh int tcp set global maxsynretransmissions=1

# Less aggressive (more patient)
netsh int tcp set global maxsynretransmissions=4
```

### 1.9 ECN (Explicit Congestion Notification)

ECN allows routers to signal congestion without dropping packets. Requires both endpoints and
intermediate routers to support it. Benefits are marginal on most consumer networks.

```powershell
# Enable ECN
netsh int tcp set global ecncapability=enabled

# Disable ECN (default)
netsh int tcp set global ecncapability=disabled
```

**Recommendation:** Disable unless you are on a network where ECN is known to be supported
(data center, cloud environments).

### 1.10 Additional TCP Global Settings

```powershell
# DCA (Direct Cache Access) - offload packet data to CPU cache
netsh int tcp set global dca=enabled

# SACK (Selective Acknowledgement) - always keep enabled
netsh int tcp set global sack=enabled

# RSC (Receive Segment Coalescing) - combine received segments
netsh int tcp set global rsc=enabled

# Set congestion control provider
netsh int tcp set global congestionprovider=default
netsh int tcp set global congestionprovider=ctcp      # Compound TCP
netsh int tcp set global congestionprovider=newreno   # NewReno
```

### 1.11 Recommended Low-Latency Profile

```powershell
netsh int tcp set global autotuninglevel=restricted
netsh int tcp set global rss=enabled
netsh int tcp set global chimney=disabled
netsh int tcp set global timestamps=disabled
netsh int tcp set global initialRto=3000
netsh int tcp set global nonsackrttresiliency=disabled
netsh int tcp set global maxsynretransmissions=2
netsh int tcp set global ecncapability=disabled
netsh int tcp set global dca=enabled
netsh int tcp set global sack=enabled
netsh int tcp set global rsc=enabled
netsh int tcp set global congestionprovider=ctcp
```

---

## 2. Nagle's Algorithm

Nagle's algorithm batches small TCP segments to reduce overhead on the wire. It is beneficial for
bulk transfers but harmful for latency-sensitive applications (gaming, SSH, database connections,
real-time communication).

When Nagle is enabled, small outgoing packets are delayed (up to 200ms or until an ACK arrives)
to be coalesced into a larger segment. For interactive applications, this adds perceptible latency.

### 2.1 Disable Nagle Per Network Adapter

The keys are located under:

```
HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{INTERFACE-GUID}
```

Two values control Nagle:

| Value              | Type    | Effect                                                |
|--------------------|---------|-------------------------------------------------------|
| `TcpAckFrequency`  | DWORD   | Number of packets before an ACK is sent. 1 = ACK every packet. |
| `TcpNoDelay`       | DWORD   | 1 = disable Nagle for this interface.                  |

```powershell
# List all network interface GUIDs
Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces" |
    ForEach-Object { $_.PSChildName }

# Disable Nagle on a specific adapter (replace {GUID} with actual adapter GUID)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}" `
    -Name "TcpAckFrequency" -Value 1 -Type DWord
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}" `
    -Name "TcpNoDelay" -Value 1 -Type DWord
```

### 2.2 Disable Nagle on All Adapters (PowerShell)

```powershell
# Disable Nagle on ALL network adapters
$interfaces = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"

foreach ($iface in $interfaces) {
    Set-ItemProperty -Path $iface.PSPath -Name "TcpAckFrequency" -Value 1 -Type DWord
    Set-ItemProperty -Path $iface.PSPath -Name "TcpNoDelay" -Value 1 -Type DWord
    Write-Host "Nagle disabled on: $($iface.PSChildName)"
}

# Re-enable Nagle on all adapters
foreach ($iface in $interfaces) {
    Remove-ItemProperty -Path $iface.PSPath -Name "TcpAckFrequency" -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $iface.PSPath -Name "TcpNoDelay" -ErrorAction SilentlyContinue
    Write-Host "Nagle re-enabled on: $($iface.PSChildName)"
}
```

### 2.3 Disable Nagle for MSMQ

Microsoft Message Queuing has its own Nagle-like batching:

```powershell
# Disable Nagle for MSMQ
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\MSMQ\Parameters" `
    -Name "TCPNoDelay" -Value 1 -Type DWord
```

### 2.4 When to Disable Nagle

| Scenario                     | Disable Nagle? | Reason                                          |
|------------------------------|----------------|--------------------------------------------------|
| Online gaming                | Yes            | Input commands must be sent immediately           |
| SSH / terminal sessions      | Yes            | Keystroke echo must be instant                    |
| Database connections         | Yes            | Small queries need immediate response             |
| VoIP / video conferencing    | Yes            | Audio/video packets cannot be delayed              |
| Real-time trading            | Yes            | Every millisecond matters                         |
| Web browsing (casual)        | Optional       | Minor impact, but can help page load feel snappier |
| Bulk file transfers          | No             | Nagle improves throughput by reducing overhead     |
| Backups / large uploads      | No             | Batching is beneficial                             |
| Streaming media              | No             | Large buffered transfers are not affected          |

---

## 3. DNS Optimization

DNS resolution is often the first thing a user perceives as "slowness." Faster DNS servers and
proper caching reduce page-load latency.

### 3.1 View Current DNS Configuration

```powershell
# View DNS servers for all adapters
Get-DnsClientServerAddress | Format-Table InterfaceAlias, ServerAddresses -AutoSize

# View DNS cache parameters
Get-DnsClientGlobalSetting

# Test DNS resolution speed
 Measure-Command { Resolve-DnsName "www.google.com" -Type A } | Select-Object TotalMilliseconds
```

### 3.2 Popular DNS Provider IPs

| Provider       | Primary      | Secondary    | IPv6 (Primary)     | IPv6 (Secondary)   | Features               |
|----------------|--------------|--------------|--------------------|--------------------|------------------------|
| Cloudflare     | `1.1.1.1`   | `1.0.0.1`   | `2606:4700:4700::1111` | `2606:4700:4700::1001` | Privacy, fast          |
| Google         | `8.8.8.8`   | `8.8.4.4`   | `2001:4860:4860::8888` | `2001:4860:4860::8844` | Reliable, global       |
| Quad9          | `9.9.9.9`   | `149.112.112.112` | `2620:fe::fe`  | `2620:fe::9`       | Security (malware blocking) |
| OpenDNS        | `208.67.222.222` | `208.67.220.220` | `2620:119:35::35` | `2620:119:53::53` | Parental controls      |
| AdGuard        | `94.140.14.14` | `94.140.15.15` | `2a10:50c0::ad1:ff` | `2a10:50c0::ad2:ff` | Ad blocking            |
| NextDNS        | Custom       | Custom       | Custom              | Custom              | Configurable blocking  |
| Control D      | Custom       | Custom       | Custom              | Custom              | Customizable filtering |
| OpenDNS FamilyShield | `208.67.222.123` | `208.67.220.123` | `2620:119:35::123` | `2620:119:53::53` | Adult content blocking |

### 3.3 Set DNS Servers

#### Using netsh

```powershell
# Set DNS on a specific interface (IPv4)
netsh interface ip set dns "Ethernet" static 1.1.1.1 primary
netsh interface ip add dns "Ethernet" 1.0.0.1 index=2

# Set DNS on Wi-Fi
netsh interface ip set dns "Wi-Fi" static 1.1.1.1 primary
netsh interface ip add dns "Wi-Fi" 1.0.0.1 index=2

# Set DNS to automatic (DHCP)
netsh interface ip set dns "Ethernet" dhcp
```

#### Using PowerShell

```powershell
# Set DNS on a specific adapter
Set-DnsClientServerAddress -InterfaceAlias "Ethernet" `
    -ServerAddresses ("1.1.1.1", "1.0.0.1")

# Set DNS on Wi-Fi
Set-DnsClientServerAddress -InterfaceAlias "Wi-Fi" `
    -ServerAddresses ("9.9.9.9", "149.112.112.112")

# Set DNS on ALL adapters at once
$adapters = Get-NetAdapter | Where-Object { $_.Status -eq "Up" }
foreach ($adapter in $adapters) {
    Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex `
        -ServerAddresses ("1.1.1.1", "1.0.0.1")
    Write-Host "DNS set on: $($adapter.Name)"
}

# Reset to DHCP-assigned DNS
Set-DnsClientServerAddress -InterfaceAlias "Ethernet" -ResetServerAddresses
```

### 3.4 DNS-over-HTTPS (DoH)

DoH encrypts DNS queries via HTTPS, preventing eavesdropping and manipulation. Windows 10 1903+
and Windows 11 support DoH natively.

#### Verify DoH Provider Templates

```powershell
# List known DoH templates
Get-DnsClientDohServerAddress | Format-Table ServerAddress, DohTemplate, AllowFallbackToUdp, AutoUpgradeToHttps
```

#### Add Custom DoH Server via Registry

```powershell
# Add Cloudflare DoH (template-based)
# Key: HKLM:\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\Doh\TryHarderDnsServers

# Step 1: Add the server to the known list
Add-DnsClientDohServerAddress -ServerAddress "1.1.1.1" `
    -DohTemplate "https://cloudflare-dns.com/dns-query" `
    -AllowFallbackToUdp $false `
    -AutoUpgradeToHttps $true

Add-DnsClientDohServerAddress -ServerAddress "1.0.0.1" `
    -DohTemplate "https://cloudflare-dns.com/dns-query" `
    -AllowFallbackToUdp $false `
    -AutoUpgradeToHttps $true

# Step 2: Set it as the DNS server (DoH will be used if template is recognized)
Set-DnsClientServerAddress -InterfaceAlias "Ethernet" `
    -ServerAddresses ("1.1.1.1", "1.0.0.1")
```

#### Manual Registry DoH Setup

```powershell
# For providers not in the built-in list, configure via the Settings UI:
# Settings > Network & Internet > [Adapter] > DNS > Encrypted only (DNS over HTTPS)

# Or via registry for a custom provider:
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters"

# If the provider is not auto-detected, you must use the GUI or:
netsh interface ipv4 set dnsservers "Ethernet" static 1.1.1.1 primary validate=no
```

### 3.5 DNS Caching

Windows maintains a local DNS cache to avoid repeated lookups for frequently accessed domains.

```powershell
# View cached DNS entries
ipconfig /displaydns | Select-String "Record Name" | Select-Object -First 20

# Flush the DNS cache
Clear-DnsClientCache

# View DNS Client Service status
Get-Service Dnscache | Format-Table Name, Status, StartType

# Disable negative caching (cache failed lookups shorter)
Set-DnsClientGlobalSetting -MaxCacheTtl 3600       # 1 hour max positive TTL
Set-DnsClientGlobalSetting -MaxNegativeCacheTtl 300 # 5 min negative cache
```

**WARNING:** Never disable the Dnscache service. Disabling it does not reduce DNS traffic; it
causes repeated DNS queries for every connection and breaks other Windows services. Always keep
it set to Automatic.

### 3.6 Benchmark DNS Speed

```powershell
# Quick benchmark of multiple DNS servers
$servers = @("1.1.1.1", "8.8.8.8", "9.9.9.9", "208.67.222.222", "94.140.14.14")
$domains = @("www.google.com", "www.github.com", "www.cloudflare.com")

foreach ($server in $servers) {
    $totalMs = 0
    foreach ($domain in $domains) {
        $time = (Measure-Command {
            Resolve-DnsName $domain -Server $server -Type A -DnsOnly -ErrorAction SilentlyContinue
        }).TotalMilliseconds
        $totalMs += $time
    }
    Write-Host "DNS $server : $([math]::Round($totalMs, 1))ms total (avg: $([math]::Round($totalMs / $domains.Count, 1))ms)"
}
```

---

## 4. NIC Advanced Properties

Modern NICs expose dozens of tunable parameters. Property names vary by vendor (Intel, Realtek,
Killer/Etheros, Broadcom). Use Device Manager or PowerShell to inspect and modify.

### 4.1 View NIC Properties via PowerShell

```powershell
# List all advanced properties for a specific NIC
Get-NetAdapterAdvancedProperty -Name "Ethernet" | Format-Table DisplayName, DisplayValue, RegistryKeyword, RegistryValue -AutoSize

# Filter for a specific property
Get-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "*Buffer*" | Format-List

# List all advanced properties across all adapters
Get-NetAdapterAdvancedProperty | Format-Table Name, DisplayName, DisplayValue -AutoSize
```

### 4.2 Set NIC Properties

```powershell
# Set a specific advanced property
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Jumbo Packet" -RegistryValue "9014"

# Set Interrupt Moderation Rate
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Interrupt Moderation Rate" -RegistryValue "Adaptive"

# Set Receive Buffers
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Receive Buffers" -RegistryValue 1024

# Set Transmit Buffers
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Transmit Buffers" -RegistryValue 1024
```

### 4.3 Key NIC Properties

#### Throughput Buffer Size / Jumbo Frame

Jumbo frames allow MTU larger than 1500 bytes (up to 9014 bytes), reducing per-packet overhead
for large transfers on LANs.

```powershell
# Enable jumbo frames (must be configured on BOTH ends of the link)
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Jumbo Packet" -RegistryValue "9014"

# Disable jumbo frames (default)
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Jumbo Packet" -RegistryValue "1514"

# Common jumbo frame sizes: 1514, 4088, 9014
```

**IMPORTANT:** Jumbo frames must match on both the sending and receiving NIC. If the switch or
remote host does not support jumbo frames, packets will be dropped. Only enable on isolated LANs
where all devices support the same MTU.

#### Interrupt Moderation / Moderation Rate

Interrupt moderation batches NIC interrupts to reduce CPU overhead. Too aggressive moderation
increases latency; too little increases CPU usage.

| Setting     | Effect                                        |
|-------------|-----------------------------------------------|
| Disabled    | Every packet triggers an interrupt (lowest latency, highest CPU) |
| Minimal     | Light batching                                |
| Adaptive    | Auto-adjusts based on load (recommended)      |
| Low         | Moderate batching                              |
| Medium      | Standard batching (default for most NICs)     |
| High        | Heavy batching (highest throughput, highest latency) |

```powershell
# Set interrupt moderation to Adaptive
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Interrupt Moderation Rate" -RegistryValue "Adaptive"

# Disable interrupt moderation (lowest latency)
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Interrupt Moderation" -RegistryValue "Disabled"
```

#### Receive Buffers / Transmit Buffers

Buffer sizes control how many packets the NIC can queue before the OS processes them. Larger
buffers help under high throughput but increase memory usage and can add latency.

```powershell
# Increase receive buffers (default is often 256-512)
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Receive Buffers" -RegistryValue 1024

# Increase transmit buffers
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Transmit Buffers" -RegistryValue 1024

# Intel NICs may also have:
# "Maximum number of RSS Processors" - set to match your CPU core count
# "Number of RSS Queues" - set to match or exceed CPU cores
```

#### Flow Control

Flow control pauses packet transmission when the receiver is overwhelmed. Beneficial in congested
networks but can hurt performance when there is no congestion.

```powershell
# Disable flow control (often best for gaming / interactive use)
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Flow Control" -RegistryValue "Disabled"

# Enable flow control in both directions
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Flow Control" -RegistryValue "Rx & Tx Enabled"

# Options: Disabled, Rx Enabled, Tx Enabled, Rx & Tx Enabled
```

#### Energy Efficient Ethernet (EEE) / Power Saving Mode

EEE puts the PHY into a low-power state during idle periods. Saves power but adds wake-up
latency (can be several milliseconds). Disable for latency-sensitive workloads.

```powershell
# Disable Energy Efficient Ethernet
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Energy Efficient Ethernet" -RegistryValue "Disabled"

# Disable power saving on the adapter
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Power Saving Mode" -RegistryValue "Disabled"
```

#### Power Management

```powershell
# Disable NIC power management via device manager registry
# This prevents the OS from turning off the NIC to save power

$adapters = Get-WmiObject MSPower_DeviceEnable -Namespace root\wmi
foreach ($adapter in $adapters) {
    if ($adapter.InstanceName -like "**_TCPIP*") {
        $adapter.Enable = $false
        $adapter.Put()
    }
}
```

#### Speed & Duplex

For gigabit+ connections, always use "Auto Negotiation" unless both endpoints support fixed speed
and you have ruled out negotiation problems.

```powershell
# Set to auto-negotiation (default, recommended)
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Speed & Duplex" -RegistryValue "Auto Negotiation"

# Force specific speed (diagnostic use only)
# Values vary by NIC: "10 Mbps Full Duplex", "100 Mbps Full Duplex", "1.0 Gbps Full Duplex", "2.5 Gbps Full Duplex"
Set-NetAdapterAdvancedProperty -Name "Ethernet" -DisplayName "Speed & Duplex" -RegistryValue "1.0 Gbps Full Duplex"
```

### 4.4 Vendor-Specific Property Names

| Feature            | Intel                      | Realtek                    | Killer/Etheros              |
|--------------------|----------------------------|----------------------------|-----------------------------|
| Jumbo Frame        | Jumbo Packet               | Jumbo Frame                | Jumbo Packet                |
| Interrupt Mod.     | Interrupt Moderation       | Interrupt Moderation       | Interrupt Moderation        |
| Flow Control       | Flow Control               | Flow Control               | Flow Control                |
| EEE                | Energy Efficient Ethernet  | Green Ethernet             | Energy Efficient Ethernet   |
| Receive Buffers    | Receive Buffers            | Receive Buffers            | Receive Buffers             |
| Transmit Buffers   | Transmit Buffers           | Transmit Buffers           | Transmit Buffers            |
| RSS Queues         | Number of RSS Queues       | Receive Side Scaling       | RSS Queues                  |
| Speed & Duplex     | Speed & Duplex             | Speed & Duplex             | Speed & Duplex              |
| Offload Checksum   | Checksum Offload           | Checksum Offload           | Checksum Offload            |
| Offload LSO        | Large Send Offload         | Large Send Offload         | Large Send Offload          |

---

## 5. Network Throttling Index

Windows includes a network throttling mechanism originally designed to prevent non-multimedia
network traffic from interfering with media streaming. It can throttle general TCP throughput.

### 5.1 Network Throttling Index

The index controls the ratio of non-multimedia to multimedia network traffic allowed.

| Value       | Effect                                                      |
|-------------|--------------------------------------------------------------|
| `10`        | Default. Non-multimedia traffic throttled to 10 packets per ms |
| `20`        | Slightly less throttling                                      |
| `ffffffff`  | Disabled (unlimited)                                         |

```powershell
# Disable network throttling
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" `
    -Name "NetworkThrottlingIndex" -Value 0xFFFFFFFF -Type DWord

# Re-enable default throttling
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" `
    -Name "NetworkThrottlingIndex" -Value 10 -Type DWord

# Verify
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" `
    -Name "NetworkThrottlingIndex"
```

### 5.2 SystemProfile Registry Settings

The full `SystemProfile` key also contains scheduling and priority settings:

```powershell
$profilePath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"

# View all values
Get-ItemProperty $profilePath | Format-List

# Gaming priority boost (set foreground to highest priority)
Set-ItemProperty -Path $profilePath -Name "NetworkThrottlingIndex" -Value 0xFFFFFFFF -Type DWord
Set-ItemProperty -Path $profilePath -Name "SystemResponsiveness" -Value 0 -Type DWord
Set-ItemProperty -Path $profilePath -Name "NoLazyMode" -Value 1 -Type DWord
Set-ItemProperty -Path $profilePath -Name "LazyModeTimeout" -Value 0 -Type DWord

# Profile subkey for gaming
$gamePath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"
Set-ItemProperty -Path $gamePath -Name "Affinity" -Value 0 -Type DWord
Set-ItemProperty -Path $gamePath -Name "Background Only" -Value "False" -Type String
Set-ItemProperty -Path $gamePath -Name "Clock Rate" -Value 10000 -Type DWord
Set-ItemProperty -Path $gamePath -Name "GPU Priority" -Value 8 -Type DWord
Set-ItemProperty -Path $gamePath -Name "Priority" -Value 6 -Type DWord
Set-ItemProperty -Path $gamePath -Name "Scheduling Category" -Value "High" -Type String
Set-ItemProperty -Path $gamePath -Name "SFIO Priority" -Value "High" -Type String
```

### 5.3 Game Mode Scheduling

Windows Game Mode prioritizes the foreground game and dedicates resources to it.

```powershell
# Game Mode is typically toggled via Settings > Gaming > Game Mode
# Registry equivalent:
$gameModePath = "HKCU:\Software\Microsoft\GameBar"
Set-ItemProperty -Path $gameModePath -Name "AllowAutoGameMode" -Value 1 -Type DWord
Set-ItemProperty -Path $gameModePath -Name "AutoGameModeEnabled" -Value 1 -Type DWord

# Verify game bar settings
Get-ItemProperty $gameModePath -Name "AllowAutoGameMode", "AutoGameModeEnabled"
```

### 5.4 Disable Nagle + Throttling Together (Gaming Profile)

```powershell
# Combined gaming optimization script
Write-Host "=== Applying Gaming Network Profile ==="

# Disable network throttling
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" `
    -Name "NetworkThrottlingIndex" -Value 0xFFFFFFFF -Type DWord
Write-Host "[OK] Network throttling disabled"

# System responsiveness
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" `
    -Name "SystemResponsiveness" -Value 0 -Type DWord
Write-Host "[OK] System responsiveness set to 0"

# Disable Nagle on all adapters
$interfaces = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"
foreach ($iface in $interfaces) {
    Set-ItemProperty -Path $iface.PSPath -Name "TcpAckFrequency" -Value 1 -Type DWord
    Set-ItemProperty -Path $iface.PSPath -Name "TcpNoDelay" -Value 1 -Type DWord
}
Write-Host "[OK] Nagle disabled on all interfaces"

# TCP optimization
netsh int tcp set global autotuninglevel=restricted | Out-Null
netsh int tcp set global timestamps=disabled | Out-Null
netsh int tcp set global ecncapability=disabled | Out-Null
netsh int tcp set global congestionprovider=ctcp | Out-Null
Write-Host "[OK] TCP stack optimized for low latency"

Write-Host "=== Gaming profile applied. Restart for full effect. ==="
```

---

## 6. Delivery Optimization

Windows Update and Microsoft Store use Delivery Optimization (DO) to download updates. By default,
this may use peer-to-peer (P2P) downloading which consumes bandwidth and can interfere with
application traffic.

### 6.1 View Delivery Optimization Settings

```powershell
# View current DO configuration
Get-DeliveryOptimization | Format-List

# View DO status
Get-DeliveryOptimizationStatus | Format-List

# View DO per-file status
Get-DeliveryOptimizationStatus | Select-Object FileId, FileSize, TotalBytesDownloaded, IsP2PEnabled
```

### 6.2 Delivery Optimization Registry Settings

```powershell
$doPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization"

# Ensure the policy key exists
if (-not (Test-Path $doPath)) {
    New-Item -Path $doPath -Force | Out-Null
}

# DODownloadMode values:
#   0   = Off (no DO, downloads from Microsoft only)
#   1   = LAN (peer download from same LAN only)
#   2   = Group (peer download from same AD domain or cloud group)
#   100 = Background (background downloads only, no foreground P2P)

# Disable P2P entirely (download from Microsoft only)
Set-ItemProperty -Path $doPath -Name "DODownloadMode" -Value 0 -Type DWord

# Allow LAN-only peers (less bandwidth risk)
Set-ItemProperty -Path $doPath -Name "DODownloadMode" -Value 1 -Type DWord

# Set download mode via PowerShell cmdlet
Set-DeliveryOptimization -DODownloadMode 0

# Additional DO settings
Set-ItemProperty -Path $doPath -Name "DOGroupID" -Value "{YOUR-GROUP-GUID}" -Type String

# Restrict DO bandwidth
Set-ItemProperty -Path $doPath -Name "DOMaxBandwidthEnabled" -Value 1 -Type DWord
Set-ItemProperty -Path $doPath -Name "DOMaxBandwidthPercentage" -Value 10 -Type DWord  # 10% of link speed
```

### 6.3 Disable Delivery Optimization Background Upload

```powershell
# Set background upload to 0 (disable uploading to other peers)
Set-DeliveryOptimization -DOPercentageMaxBackgroundBandwidth 0

# Via registry
Set-ItemProperty -Path $doPath -Name "DOPercentageMaxBackgroundBandwidth" -Value 0 -Type DWord
Set-ItemProperty -Path $doPath -Name "DOPercentageMaxForegroundBandwidth" -Value 100 -Type DWord

# View DO bandwidth settings
Get-DeliveryOptimization | Select-Object DODownloadMode, DOPercentageMaxBackgroundBandwidth, DOPercentageMaxForegroundBandwidth
```

### 6.4 Flush Delivery Optimization Cache

```powershell
# Stop the DO service
Stop-Service DoSvc

# Delete the DO cache
Remove-Item -Path "$env:SystemDrive:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache\*" -Recurse -Force -ErrorAction SilentlyContinue

# Restart the service
Start-Service DoSvc
```

---

## 7. Wi-Fi Optimization

Wi-Fi introduces additional latency and variability compared to Ethernet. These settings help
minimize Wi-Fi-specific overhead.

### 7.1 View Wi-Fi Settings

```powershell
# View current Wi-Fi interface details
netsh wlan show interfaces

# View all saved wireless profiles
netsh wlan show profiles

# View specific profile details
netsh wlan show profile name="YourNetwork" key=clear

# View Wi-Fi radio state
netsh wlan show radio
```

### 7.2 Wi-Fi Power Saving Mode

Power saving mode puts the Wi-Fi radio to sleep between transmissions, saving battery but
increasing latency.

```powershell
# View power saving mode
netsh wlan show powerparameter

# Disable power saving (maximum performance)
netsh wlan set powerparameter disabled

# Set power saving level (1-5, lower = more power saved, higher = better performance)
# Windows uses different names: max, medium-low, low, medium-high, min
netsh wlan set powerparameter level=lowest    # No power saving (best performance)
netsh wlan set powerparameter level=medium    # Balanced
netsh wlan set powerparameter level=highest   # Maximum power saving (worst performance)
```

#### Via Registry (more granular)

```powershell
# Wi-Fi power saving registry
$wlanPath = "HKLM:\SYSTEM\CurrentControlSet\Services\WlanSvc\Parameters"

# Set power saving mode
# 1 = Maximum Performance (no power saving)
# 2 = Low Power Saving
# 3 = Medium Power Saving
# 4 = Maximum Power Saving

# Via Device Manager power management
# Open Device Manager > Network adapters > [Wi-Fi adapter] > Properties > Power Management
# Uncheck "Allow the computer to turn off this device to save power"
```

### 7.3 Band Preference (5GHz vs 2.4GHz)

5GHz offers faster speeds and less interference; 2.4GHz offers better range and wall penetration.

```powershell
# View available networks and their bands
netsh wlan show networks mode=bssid

# Prefer 5GHz (set preferred band via registry or adapter properties)
# In adapter Advanced Properties:
#   "Preferred Band" = "5GHz" or "2.4GHz" or "Any"

Set-NetAdapterAdvancedProperty -Name "Wi-Fi" -DisplayName "Preferred Band" -RegistryValue 2  # 1=2.4GHz, 2=5GHz

# Force specific band via netsh (limited)
netsh wlan set blockednetworks display=show

# Disable 2.4GHz band via adapter (if dual-band)
# In adapter properties, disable one of the two radio bands
```

### 7.4 Roaming Aggressiveness

Controls how aggressively the NIC switches to a stronger AP. Lower values maintain connections
longer; higher values roam sooner.

```powershell
# Set roaming aggressiveness
# 1 = Lowest (stay connected, rarely roam)
# 2 = Medium-low
# 3 = Medium (default)
# 4 = Medium-high
# 5 = Highest (roam aggressively)

Set-NetAdapterAdvancedProperty -Name "Wi-Fi" -DisplayName "Roaming Aggressiveness" -RegistryValue 3

# For gaming/latency (stay on one AP): set to 1
Set-NetAdapterAdvancedProperty -Name "Wi-Fi" -DisplayName "Roaming Aggressiveness" -RegistryValue 1
```

### 7.5 Throughput Booster

Some adapters support a throughput booster that disables power saving features.

```powershell
# Enable throughput booster (disables some power saving)
Set-NetAdapterAdvancedProperty -Name "Wi-Fi" -DisplayName "Throughput Booster" -RegistryValue "Enabled"

# Disable throughput booster (default)
Set-NetAdapterAdvancedProperty -Name "Wi-Fi" -DisplayName "Throughput Booster" -RegistryValue "Disabled"
```

### 7.6 Wi-Fi Auto-Tuning and MIMO Power Save

```powershell
# Disable Wi-Fi Sense (auto-connect to hotspots)
# Windows 10+: Settings > Network & Internet > Wi-Fi > Manage known networks
#   For each network > Properties > Connect automatically > Off

# Disable Wi-Fi Sense via registry
$wifiSensePath = "HKLM:\SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config"
Set-ItemProperty -Path $wifiSensePath -Name "AutoConnectAllowedOEM" -Value 0 -Type DWord

# Disable Wi-Fi Direct (if not used)
Set-NetAdapterAdvancedProperty -Name "Wi-Fi" -DisplayName "Wi-Fi Direct" -RegistryValue "Disabled"
```

### 7.7 Wi-Fi Channel Optimization

Use a Wi-Fi analyzer to find the least congested channel, then set it on your router. For the
Windows client, channel selection is handled by the driver.

```powershell
# View the current channel being used
netsh wlan show interfaces | Select-String "Channel"

# Force a specific channel on the adapter (not recommended, use auto)
# Most adapters do not expose channel selection on the client side
# Configure on the router/access point instead

# View channel utilization
netsh wlan show networks mode=bssid | Select-String "Channel"
```

---

## 8. Netsh Commands Reference

### 8.1 Netsh Interface IP Commands

```powershell
# Show IP configuration
netsh interface ip show config

# Show address configuration for specific adapter
netsh interface ip show config name="Ethernet"

# Set static IP
netsh interface ip set address "Ethernet" static 192.168.1.100 255.255.255.0 192.168.1.1

# Set DHCP
netsh interface ip set address "Ethernet" dhcp

# Add secondary IP address
netsh interface ip add address "Ethernet" 192.168.1.101 255.255.255.0

# Delete an IP address
netsh interface ip delete address "Ethernet" 192.168.1.101

# Set DNS server
netsh interface ip set dns "Ethernet" static 1.1.1.1 primary
netsh interface ip add dns "Ethernet" 1.0.0.1 index=2

# Set DHCP DNS
netsh interface ip set dns "Ethernet" dhcp

# Show WINS servers
netsh interface ip show wins

# Show ICMP statistics
netsh interface ip show icmpstats

# Show TCP connections
netsh interface ip show tcpconnections

# Show UDP connections
netsh interface ip show udpconnections
```

### 8.2 Netsh Interface TCP Commands

```powershell
# Show all TCP global settings
netsh int tcp show global

# Show supplemental TCP settings (per-interface)
netsh int tcp show supplemental

# Set global TCP parameters
netsh int tcp set global autotuninglevel=normal
netsh int tcp set global rss=enabled
netsh int tcp set global chimney=disabled
netsh int tcp set global timestamps=disabled
netsh int tcp set global initialRto=3000
netsh int tcp set global nonsackrttresiliency=disabled
netsh int tcp set global maxsynretransmissions=2
netsh int tcp set global ecncapability=disabled
netsh int tcp set global dca=enabled
netsh int tcp set global sack=enabled
netsh int tcp set global rsc=enabled
netsh int tcp set global congestionprovider=ctcp

# Show specific helper template
netsh int tcp show supplemental template=Datacenter
netsh int tcp show supplemental template=Internet
netsh int tcp show supplemental template=Compat
netsh int tcp show supplemental template=DatacenterCustom
netsh int tcp show supplemental template=InternetCustom

# Set congestion control on a specific template
netsh int tcp set supplemental template=Internet congestionprovider=ctcp

# Show TCP connection statistics
netsh int tcp show stats

# Show TCP global statistics
netsh int tcp show globalstats
```

### 8.3 Netsh Interface IPv6 Commands

```powershell
# Show IPv6 configuration
netsh interface ipv6 show config

# Set IPv6 address
netsh interface ipv6 set address "Ethernet" 2001:db8::100

# Set IPv6 DNS
netsh interface ipv6 set dns "Ethernet" static 2606:4700:4700::1111 primary
netsh interface ipv6 add dns "Ethernet" 2606:4700:4700::1001 index=2

# Show IPv6 route table
netsh interface ipv6 show route

# Add IPv6 route
netsh interface ipv6 add route prefix=::/0 interface="Ethernet" nexthop=2001:db8::1

# Show IPv6 neighbors (ARP equivalent)
netsh interface ipv6 show neighbors

# Disable IPv6 (if needed, not recommended)
netsh interface ipv6 set interface "Ethernet" disabled
```

### 8.4 Netsh Interface Firewall Commands

```powershell
# Show firewall configuration
netsh firewall show config

# Show current firewall state
netsh firewall show state

# Enable/disable firewall
netsh firewall set opmode enable
netsh firewall set opmode disable

# Add firewall rule (legacy)
netsh firewall add portopening TCP 8080 "My Web App"

# Delete firewall rule (legacy)
netsh firewall delete portopening TCP 8080
```

### 8.5 Netsh WLAN Commands

```powershell
# Show all wireless interfaces
netsh wlan show interfaces

# Show wireless drivers and capabilities
netsh wlan show drivers

# Show all stored wireless profiles
netsh wlan show profiles

# Show a specific profile (with key in clear text if admin)
netsh wlan show profile name="NetworkName" key=clear

# Show available wireless networks
netsh wlan show networks
netsh wlan show networks mode=bssid

# Connect to a network
netsh wlan connect name="NetworkName"

# Disconnect from wireless
netsh wlan disconnect

# Delete a wireless profile
netsh wlan delete profile name="NetworkName"

# Export a wireless profile
netsh wlan export profile name="NetworkName" folder="C:\Backups"

# Import a wireless profile
netsh wlan add profile filename="C:\Backups\NetworkName.xml" user=all

# Set auto-config
netsh wlan set autoconfig enabled=yes interface="Wi-Fi"

# Block a specific network
netsh wlan add blockperiod interface="Wi-Fi" index=1

# Show radio information
netsh wlan show radio

# Set power saving mode
netsh wlan set powerparameter disabled

# Show network conditions (for troubleshooting)
netsh wlan show networks mode=bssid | findstr /C:"Signal" /C:"Channel" /C:"Authentication"
```

### 8.6 Netsh Interface Port Proxy (Port Forwarding)

```powershell
# Add port forwarding (local to remote)
netsh interface portproxy add v4tov4 listenport=8080 listenaddress=0.0.0.0 connectport=80 connectaddress=192.168.1.50

# Add port forwarding with specific source
netsh interface portproxy add v4tov4 listenport=3389 listenaddress=0.0.0.0 connectport=3389 connectaddress=192.168.1.100

# Show all port proxies
netsh interface portproxy show all

# Delete a port proxy
netsh interface portproxy delete v4tov4 listenport=8080 listenaddress=0.0.0.0

# Delete all port proxies
netsh interface portproxy reset
```

### 8.7 Netsh Command Switches

```powershell
# Show detailed help for any command
netsh int tcp /?
netsh int tcp set global /?
netsh wlan /?

# Common switches across netsh contexts:
#   add       - Add entries
#   delete    - Remove entries
#   set       - Modify entries
#   show      - Display entries
#   reset     - Reset to defaults
#   dump      - Show current configuration as commands

# Export current config as a script
netsh dump > C:\NetworkConfig.txt
netsh int tcp dump > C:\TCPConfig.txt
```

---

## 9. MTU Optimization

MTU (Maximum Transmission Unit) is the largest packet size that can be transmitted without
fragmentation. The default is 1500 bytes for Ethernet and varies for other media. Setting the
correct MTU avoids fragmentation and improves throughput.

### 9.1 Finding the Optimal MTU

Use `ping` with the Don't Fragment flag to find the largest packet size that passes through
without fragmentation.

```powershell
# Test MTU to Google DNS (adjust size downward until ping succeeds)
ping -f -l 1472 8.8.8.8    # 1472 + 28 (IP+ICMP headers) = 1500 (standard MTU)
ping -f -l 1464 8.8.8.8    # 1464 + 28 = 1492 (PPPoE)
ping -f -l 1400 8.8.8.8    # Testing smaller sizes

# The formula: MTU = largest successful ping size + 28 (20 bytes IP header + 8 bytes ICMP header)
# For PPPoE connections, typical optimal MTU is 1492
# For standard Ethernet, typical optimal MTU is 1500
# For VPN, typical optimal MTU is 1400-1450

# Script to find optimal MTU
function Find-OptimalMTU {
    param(
        [string]$Target = "8.8.8.8",
        [int]$Start = 1472,
        [int]$End = 500
    )

    for ($size = $Start; $size -ge $End; $size -= 10) {
        $result = ping -f -l $size -n 2 $Target 2>&1
        if ($result -match "Reply from") {
            $mtu = $size + 28
            Write-Host "Optimal MTU for $Target is $mtu (payload: $size)"
            return $mtu
        }
    }
    Write-Host "Could not determine optimal MTU"
    return 0
}

# Run the function
Find-OptimalMTU -Target "8.8.8.8"
```

### 9.2 Setting MTU

```powershell
# View current MTU for all interfaces
netsh interface ipv4 show subinterfaces

# Set MTU on a specific interface (persistent across reboots)
netsh interface ipv4 set subinterface "Wi-Fi" mtu=1492 store=persistent

# Set MTU on Ethernet
netsh interface ipv4 set subinterface "Ethernet" mtu=1500 store=persistent

# Set MTU for a VPN adapter
netsh interface ipv4 set subinterface "VPN Connection" mtu=1400 store=persistent

# Set MTU via PowerShell (temporary, lost on reboot)
netsh interface ipv4 set subinterface "Ethernet" mtu=1500 store=active

# Verify the change
netsh interface ipv4 show subinterfaces
```

### 9.3 Common MTU Values

| Connection Type    | Typical MTU | Notes                              |
|--------------------|-------------|------------------------------------|
| Ethernet           | 1500        | Standard, rarely needs changing    |
| PPPoE (DSL)        | 1492        | 8 bytes PPPoE header overhead      |
| VPN (L2TP/IPSec)   | 1400-1450   | Encryption adds overhead           |
| VPN (WireGuard)    | 1420        | WireGuard overhead                 |
| VPN (OpenVPN UDP)  | 1400        | Varies by config                   |
| VPN (OpenVPN TCP)  | 1400        | Same overhead as UDP               |
| SSTP VPN           | 1400        | SSL overhead                       |
| GRE Tunnel         | 1476        | 24 bytes GRE header                |
| Wi-Fi              | 1500        | Same as Ethernet                   |
| LTE/4G             | 1430-1460   | Cellular overhead varies           |
| 6to4 Tunnel        | 1480        | 20 bytes IPv6 header               |

### 9.4 Setting IPv6 MTU

```powershell
# View IPv6 MTU
netsh interface ipv6 show subinterfaces

# Set IPv6 MTU
netsh interface ipv6 set subinterface "Ethernet" mtu=1500 store=persistent

# Set IPv6 MTU for tunnel interfaces
netsh interface ipv6 set subinterface "isatap.{GUID}" mtu=1480 store=persistent
```

### 9.5 PMTU Discovery

Path MTU Discovery (PMTUD) allows TCP to automatically discover the optimal MTU. Ensure it
is not blocked by firewalls.

```powershell
# Enable PMTU Black Hole Detection (default in Windows 10/11)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" `
    -Name "EnablePMTUDiscovery" -Value 1 -Type DWord

Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" `
    -Name "EnablePMTUBHDetect" -Value 1 -Type DWord

# Verify
Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" `
    -Name "EnablePMTUDiscovery", "EnablePMTUBHDetect"
```

---

## 10. Revert / Rollback

### 10.1 Revert TCP Global Settings to Defaults

```powershell
# Reset all TCP global settings to Windows defaults
netsh int tcp set global autotuninglevel=normal
netsh int tcp set global rss=enabled
netsh int tcp set global chimney=disabled
netsh int tcp set global timestamps=enabled
netsh int tcp set global initialRto=3000
netsh int tcp set global nonsackrttresiliency=disabled
netsh int tcp set global maxsynretransmissions=2
netsh int tcp set global ecncapability=disabled
netsh int tcp set global dca=enabled
netsh int tcp set global sack=enabled
netsh int tcp set global rsc=enabled
netsh int tcp set global congestionprovider=default
```

### 10.2 Revert Nagle's Algorithm

```powershell
# Re-enable Nagle on all adapters (remove the custom values)
$interfaces = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"
foreach ($iface in $interfaces) {
    Remove-ItemProperty -Path $iface.PSPath -Name "TcpAckFrequency" -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $iface.PSPath -Name "TcpNoDelay" -ErrorAction SilentlyContinue
}
Write-Host "Nagle re-enabled on all interfaces"

# Remove MSMQ override
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\MSMQ\Parameters" `
    -Name "TCPNoDelay" -ErrorAction SilentlyContinue
```

### 10.3 Revert Network Throttling

```powershell
# Restore default network throttling
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" `
    -Name "NetworkThrottlingIndex" -Value 10 -Type DWord

Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" `
    -Name "SystemResponsiveness" -Value 20 -Type DWord
```

### 10.4 Revert Delivery Optimization

```powershell
# Restore DO to defaults
Set-DeliveryOptimization -DODownloadMode 1

# Or via registry
Set-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization" `
    -Name "DODownloadMode" -Value 1 -Type DWord
```

### 10.5 Revert MTU

```powershell
# Reset MTU to default (1500 for most adapters)
netsh interface ipv4 set subinterface "Wi-Fi" mtu=1500 store=persistent
netsh interface ipv4 set subinterface "Ethernet" mtu=1500 store=persistent
```

### 10.6 Reset DNS to DHCP

```powershell
# Reset DNS to automatic (DHCP) on all adapters
$adapters = Get-NetAdapter | Where-Object { $_.Status -eq "Up" }
foreach ($adapter in $adapters) {
    Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ResetServerAddresses
    Write-Host "DNS reset to DHCP on: $($adapter.Name)"
}
```

### 10.7 Full Network Reset (Nuclear Option)

```powershell
# Full network reset (Windows 10 1607+ / Windows 11)
# This removes and reinstalls all network adapters
# WARNING: You will lose custom adapter configurations

# Via GUI: Settings > Network & Internet > Status > Network Reset

# Via command line (requires restart):
netsh int ip reset
netsh winsock reset
ipconfig /flushdns
ipconfig /release
ipconfig /renew

Write-Host "Restart the computer to complete the reset."
```

---

*Last updated: 2026-08-31*
