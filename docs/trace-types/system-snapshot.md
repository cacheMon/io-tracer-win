# System Snapshot

**Description:** Captures hardware and software specifications for trace context.

**Location:** `{TIMESTAMP}/system_spec/`

**Collection Method:**
- Queries system information once at trace start
- Uses Windows Management Instrumentation (WMI) and .NET APIs
- Attempts IP geolocation for country detection

## Output Files

System specifications are captured in separate JSON files:

| File | Description |
|------|-------------|
| `cpu_info.json` | CPU model, cores, frequency |
| `memory_info.json` | Total RAM, available memory, page file (swap) |
| `disk_info.json` | Storage devices, partitions, and GPUs |
| `network_info.json` | Network interfaces and addresses |
| `os_info.json` | Windows version, build, hostname, country |

---

## cpu_info.json

CPU hardware specifications.

| Field | Type | Description |
|-------|------|-------------|
| `brand` | `string` | CPU model name (from WMI `Win32_Processor`); `null` if unavailable |
| `cores_logical` | `integer` | Number of logical CPU cores (including hyperthreads) |
| `cores_physical` | `integer` | Number of physical CPU cores |
| `frequency_mhz` | `float` | Maximum CPU frequency in MHz; `null` if unavailable |
| `frequency_min_mhz` | `float` | Minimum CPU frequency in MHz; `null` (not available on Windows) |
| `frequency_max_mhz` | `float` | Maximum CPU frequency in MHz; `null` if unavailable |

### Example

```json
{
  "brand": "Intel(R) Core(TM) i7-10700 CPU @ 2.90GHz",
  "cores_logical": 16,
  "cores_physical": 8,
  "frequency_mhz": 2900.0,
  "frequency_min_mhz": null,
  "frequency_max_mhz": 2900.0
}
```

---

## memory_info.json

System memory statistics.

| Field | Type | Description |
|-------|------|-------------|
| `total_bytes` | `integer` | Total system RAM in bytes |
| `available_bytes` | `integer` | Currently available RAM in bytes |
| `used_bytes` | `integer` | Used RAM in bytes |
| `percent_used` | `float` | Memory usage percentage |
| `total_gb` | `float` | Total system RAM in GB (rounded to 2 decimals) |
| `available_gb` | `float` | Available RAM in GB (rounded to 2 decimals) |
| `swap_total_bytes` | `integer` | Total page file (swap) space in bytes |
| `swap_used_bytes` | `integer` | Used page file space in bytes |
| `swap_free_bytes` | `integer` | Free page file space in bytes |

### Example

```json
{
  "total_bytes": 17062027264,
  "available_bytes": 9073254400,
  "used_bytes": 7988772864,
  "percent_used": 46.8,
  "total_gb": 15.89,
  "available_gb": 8.45,
  "swap_total_bytes": 2147483648,
  "swap_used_bytes": 0,
  "swap_free_bytes": 2147483648
}
```

---

## disk_info.json

Storage devices and partition information.

| Field | Type | Description |
|-------|------|-------------|
| `storage_devices` | `array[string]` | List of storage devices with name, model, size (from WMI `Win32_DiskDrive`) |
| `partitions` | `array[object]` | List of mounted partitions with usage details |
| `gpus` | `array[string]` | List of GPU names (from WMI `Win32_VideoController`); empty if none detected |

### Partition Object

| Field | Type | Description |
|-------|------|-------------|
| `device` | `string` | Device path (e.g., `C:\`, `D:\`) |
| `mountpoint` | `string` | Mount point path (same as device on Windows) |
| `fstype` | `string` | Filesystem type (e.g., `NTFS`, `FAT32`) |
| `opts` | `string` | Drive type (e.g., `Fixed`, `Removable`, `CDRom`) |
| `total_bytes` | `integer` | Total partition size in bytes |
| `used_bytes` | `integer` | Used space in bytes |
| `free_bytes` | `integer` | Free space in bytes |
| `percent_used` | `float` | Usage percentage |

### Example

```json
{
  "storage_devices": [
    "Samsung SSD 980 PRO 1TB  931.51 GB",
    "WDC WD10EZEX-00W  931.51 GB"
  ],
  "partitions": [
    {
      "device": "C:\\",
      "mountpoint": "C:\\",
      "fstype": "NTFS",
      "opts": "Fixed",
      "total_bytes": 500107862016,
      "used_bytes": 125829120000,
      "free_bytes": 348827648000,
      "percent_used": 26.5
    },
    {
      "device": "D:\\",
      "mountpoint": "D:\\",
      "fstype": "NTFS",
      "opts": "Fixed",
      "total_bytes": 1000204886016,
      "used_bytes": 500102443008,
      "free_bytes": 500102443008,
      "percent_used": 50.0
    }
  ],
  "gpus": [
    "NVIDIA GeForce RTX 3080"
  ]
}
```

---

## network_info.json

Network interface information.

| Field | Type | Description |
|-------|------|-------------|
| `interfaces` | `object` | Map of interface name to interface details |
| `hostname` | `string` | System hostname |

### Interface Object

| Field | Type | Description |
|-------|------|-------------|
| `addresses` | `array[object]` | List of addresses assigned to interface |
| `is_up` | `boolean` | Whether interface is operational |
| `speed_mbps` | `integer` | Link speed in Mbps; `null` if unavailable |
| `mtu` | `integer` | Maximum transmission unit |

### Address Object

| Field | Type | Description |
|-------|------|-------------|
| `family` | `string` | Address family (`AF_INET`, `AF_INET6`, `AF_PACKET`) |
| `address` | `string` | IP or MAC address |
| `netmask` | `string` | Network mask; `null` for some address types |
| `broadcast` | `string` | Broadcast address; `null` for Windows/some types |

### Example

```json
{
  "interfaces": {
    "Loopback Pseudo-Interface 1": {
      "addresses": [
        {
          "family": "AF_INET",
          "address": "127.0.0.1",
          "netmask": "255.0.0.0",
          "broadcast": null
        },
        {
          "family": "AF_INET6",
          "address": "::1",
          "netmask": null,
          "broadcast": null
        }
      ],
      "is_up": true,
      "speed_mbps": null,
      "mtu": 0
    },
    "Ethernet": {
      "addresses": [
        {
          "family": "AF_INET",
          "address": "192.168.1.100",
          "netmask": "255.255.255.0",
          "broadcast": null
        },
        {
          "family": "AF_PACKET",
          "address": "00:1a:2b:3c:4d:5e",
          "netmask": null,
          "broadcast": null
        }
      ],
      "is_up": true,
      "speed_mbps": 1000,
      "mtu": 1500
    }
  },
  "hostname": "DESKTOP-PC"
}
```

---

## os_info.json

Operating system information.

| Field | Type | Description |
|-------|------|-------------|
| `system` | `string` | Operating system name (always `Windows`) |
| `release` | `string` | OS version (e.g., `10.0.22000`) |
| `version` | `string` | Full OS caption and build (e.g., `Microsoft Windows 10 Pro (Build 19045)`) |
| `machine` | `string` | Machine hardware architecture (`x64` or `x86`) |
| `hostname` | `string` | System hostname |
| `country` | `string` | Two-letter country code from IP geolocation; `XX` if detection fails |

### Example

```json
{
  "system": "Windows",
  "release": "10.0.22000",
  "version": "Microsoft Windows 11 Pro (Build 22000)",
  "machine": "x64",
  "hostname": "DESKTOP-PC",
  "country": "US"
}
```
