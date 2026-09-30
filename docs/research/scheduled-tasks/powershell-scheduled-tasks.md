# PowerShell Scheduled Tasks Module -- Complete Reference

All cmdlets from the `ScheduledTasks` module (available on Windows 8+ / Server 2012+). Sources: official Microsoft documentation at learn.microsoft.com.

---

## 1. Get-ScheduledTask

Retrieves task definition objects registered on a computer.

### Syntax

```powershell
Get-ScheduledTask
    [[-TaskName] <String[]>]
    [[-TaskPath] <String[]>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-TaskName` | String[] | No | One or more task names. Supports wildcards with `*`. Accepts pipeline input by property name. |
| `-TaskPath` | String[] | No | One or more paths in Task Scheduler namespace. Use `\` for root, `\*\` for wildcard. Must include leading and trailing `\`. Accepts pipeline input by property name. |
| `-CimSession` | CimSession[] | No | Remote session or computer. Alias: `Session`. |
| `-ThrottleLimit` | Int32 | No | Max concurrent operations. 0 = auto-calculate. |
| `-AsJob` | SwitchParameter | No | Run as background job. |

### Output

`CimInstance` (MSFT_ScheduledTask)

### Examples

```powershell
# Get a specific task by name
Get-ScheduledTask -TaskName "SystemScan"

# Get all tasks in a folder
Get-ScheduledTask -TaskPath "\UpdateTasks\*"

# Get tasks from multiple paths
Get-ScheduledTask -TaskPath "\Microsoft\Windows\Work Folders\","\Microsoft\Windows\Workplace Join\"

# Get ALL registered tasks on the computer
Get-ScheduledTask

# Filter by state
Get-ScheduledTask | Where-Object {$_.State -eq "Running"}

# Get only ready tasks with verbose properties
Get-ScheduledTask -TaskName "MyTask" | Format-List *

# Pipe task names
Get-ScheduledTask -TaskPath "\Custom\" | Select-Object TaskName, State, TaskPath

# Get tasks on a remote computer
$session = New-CimSession -ComputerName "Server01"
Get-ScheduledTask -CimSession $session -TaskPath "\MyTasks\"

# Export detailed task info including actions/triggers
$task = Get-ScheduledTask -TaskName "MyTask"
$task.Actions
$task.Triggers
$task.Settings
$task.Principal
```

---

## 2. Get-ScheduledTaskInfo

Retrieves run-time information: last run time, next run time, last result code, number of missed runs.

### Syntax

```powershell
# By name
Get-ScheduledTaskInfo
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# By object
Get-ScheduledTaskInfo
    [-InputObject] <CimInstance>
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-TaskName` | String | Yes (Name set) | Name of the scheduled task. |
| `-TaskPath` | String | No (Name set) | Path in Task Scheduler namespace. |
| `-InputObject` | CimInstance | Yes (Object set) | A ScheduledTask object from pipeline. |
| `-CimSession` | CimSession[] | No | Remote session or computer. |
| `-ThrottleLimit` | Int32 | No | Max concurrent operations. |
| `-AsJob` | SwitchParameter | No | Run as background job. |

### Output

`CimInstance` (MSFT_TaskDynamicInfo) -- includes `LastRunTime`, `NextRunTime`, `LastTaskResult`, `NumberOfMissedRuns`.

### Examples

```powershell
# Get run-time info for a specific task
Get-ScheduledTaskInfo -TaskName "\Sample\SchedTask01"

# Pipe tasks to get their info
Get-ScheduledTask -TaskPath "\Sample\" | Get-ScheduledTaskInfo

# Find tasks that failed on last run
Get-ScheduledTask | Get-ScheduledTaskInfo | Where-Object {$_.LastTaskResult -ne 0}

# Show last run time and result for all tasks
Get-ScheduledTask | Get-ScheduledTaskInfo | Select-Object @{N='TaskName';E={$_.TaskName}},
    LastRunTime, NextRunTime,
    @{N='Result';E={switch($_.LastTaskResult){0{'Succeeded'}default{"Failed: $($_.LastTaskResult)"}}}}

# Common result codes: 0 = success, 1 = incorrect function, 2 = file not found,
# 267011 = task not yet run, 267014 = task is currently running

# Check tasks that missed their scheduled runs
Get-ScheduledTask | Get-ScheduledTaskInfo | Where-Object {$_.NumberOfMissedRuns -gt 0}
```

---

## 3. New-ScheduledTask

Creates a task definition object in memory (does NOT register it). Use with `Register-ScheduledTask -InputObject` to register later.

### Syntax

```powershell
New-ScheduledTask
    [[-Action] <CimInstance[]>]
    [[-Description] <String>]
    [[-Principal] <CimInstance>]
    [[-Settings] <CimInstance>]
    [[-Trigger] <CimInstance[]>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-Action` | CimInstance[] | No | Array of action objects (max 32, run sequentially). |
| `-Description` | String | No | Brief description of the task. |
| `-Principal` | CimInstance | No | Security context (user/group, logon type, run level). |
| `-Settings` | CimInstance | No | Configuration for Task Scheduler behavior. |
| `-Trigger` | CimInstance[] | No | Array of trigger objects (max 48). |
| `-CimSession` | CimSession[] | No | Remote session or computer. |
| `-ThrottleLimit` | Int32 | No | Max concurrent operations. |
| `-AsJob` | SwitchParameter | No | Run as background job. |

### Output

`CimInstance` (MSFT_ScheduledTask)

### Examples

```powershell
# Define all components, create task object, register later
$action = New-ScheduledTaskAction -Execute "Taskmgr.exe"
$trigger = New-ScheduledTaskTrigger -AtLogon
$principal = "Contoso\Administrator"
$settings = New-ScheduledTaskSettingsSet
$task = New-ScheduledTask -Action $action -Principal $principal -Trigger $trigger -Settings $settings
Register-ScheduledTask T1 -InputObject $task

# Define a task with multiple actions
$actions = (New-ScheduledTaskAction -Execute 'foo.ps1'), (New-ScheduledTaskAction -Execute 'bar.ps1')
$trigger = New-ScheduledTaskTrigger -Daily -At '9:15 AM'
$principal = New-ScheduledTaskPrincipal -UserId 'DOMAIN\user' -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -RunOnlyIfNetworkAvailable -WakeToRun
$task = New-ScheduledTask -Action $actions -Principal $principal -Trigger $trigger -Settings $settings
Register-ScheduledTask 'baz' -InputObject $task

# Create task with description
$action = New-ScheduledTaskAction -Execute "notepad.exe"
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddDays(1)
$task = New-ScheduledTask -Action $action -Trigger $trigger -Description "Daily notepad launch"
Register-ScheduledTask "MyNotepad" -InputObject $task
```

---

## 4. Register-ScheduledTask

Registers (creates and installs) a scheduled task on the local computer. This is the primary cmdlet for creating tasks.

### Syntax

```powershell
# User parameter set (default)
Register-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [-Action] <CimInstance[]>
    [[-Trigger] <CimInstance[]>]
    [[-Description] <String>]
    [[-Settings] <CimInstance>]
    [[-RunLevel] <RunLevelEnum>]
    [[-User] <String>]
    [[-Password] <String>]
    [-Force]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# XML parameter set
Register-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [-Xml] <String>
    [[-User] <String>]
    [[-Password] <String>]
    [-Force]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# Principal parameter set
Register-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [-Action] <CimInstance[]>
    [[-Trigger] <CimInstance[]>]
    [[-Description] <String>]
    [[-Settings] <CimInstance>]
    [[-Principal] <CimInstance>]
    [-Force]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# Object parameter set (pipeline)
Register-ScheduledTask
    [-InputObject] <CimInstance>
    [[-TaskName] <String>]
    [[-TaskPath] <String>]
    [[-User] <String>]
    [[-Password] <String>]
    [-Force]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-TaskName` | String | Yes | Name of the scheduled task. |
| `-TaskPath` | String | No | Path in namespace. Default: root `\`. |
| `-Action` | CimInstance[] | Yes (User/Principal) | One or more action objects (max 32). |
| `-Trigger` | CimInstance[] | No | One or more trigger objects (max 48). |
| `-Description` | String | No | Task description. |
| `-Settings` | CimInstance | No | Task settings object. |
| `-RunLevel` | RunLevelEnum | No (User) | `Limited` or `Highest`. |
| `-Principal` | CimInstance | No (Principal) | Security context object from `New-ScheduledTaskPrincipal`. |
| `-User` | String | No | Account under which task runs. Ignored in Principal set. |
| `-Password` | String | No | Password for the user account. Ignored for system accounts. |
| `-Xml` | String | Yes (Xml) | XML string containing task definition. |
| `-InputObject` | CimInstance | Yes (Object) | Task definition object from pipeline. |
| `-Force` | SwitchParameter | No | Overwrite existing task without confirmation. |
| `-CimSession` | CimSession[] | No | Remote session or computer. |
| `-ThrottleLimit` | Int32 | No | Max concurrent operations. |
| `-AsJob` | SwitchParameter | No | Run as background job. |

### Output

`CimInstance` (MSFT_ScheduledTask)

### Examples

```powershell
# Register a task with action and trigger (simplest form)
$Time = New-ScheduledTaskTrigger -At 12:00 -Once
$PS = New-ScheduledTaskAction -Execute "PowerShell.exe"
Register-ScheduledTask -TaskName "SoftwareScan" -Trigger $Time -User "Contoso\Administrator" -Action $PS

# Register with full principal object (RunLevel Highest)
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-File C:\Scripts\backup.ps1"
$trigger = New-ScheduledTaskTrigger -Daily -At "2:00 AM"
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName "NightlyBackup" -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description "Runs nightly system backup"

# Register from XML string
$xml = Export-ScheduledTask -TaskName "ExistingTask"
Register-ScheduledTask -TaskName "CopyOfExisting" -Xml $xml -Force

# Register with -Force to overwrite
Register-ScheduledTask -TaskName "MyTask" -Action $action -Trigger $trigger -User "SYSTEM" -Force

# Register with password for a domain user
Register-ScheduledTask -TaskName "DomainTask" -Action $action -Trigger $trigger -User "DOMAIN\jsmith" -Password "P@ssw0rd"

# Register to run as NETWORK SERVICE
Register-ScheduledTask -TaskName "NetTask" -Action $action -Trigger $trigger -User "NT AUTHORITY\NETWORKSERVICE"

# Register with multiple triggers
$trigger1 = New-ScheduledTaskTrigger -Daily -At "8:00 AM"
$trigger2 = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday,Friday -At "5:00 PM"
Register-ScheduledTask -TaskName "MultiTrigger" -Action $action -Trigger $trigger1,$trigger2 -User "SYSTEM"

# Register from a task definition object
$task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings
Register-ScheduledTask -TaskName "FromObject" -InputObject $task
```

---

## 5. Set-ScheduledTask

Modifies an existing registered task. Changes do not affect a currently running instance.

### Syntax

```powershell
# User parameter set (default)
Set-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [[-Action] <CimInstance[]>]
    [[-Trigger] <CimInstance[]>]
    [[-Settings] <CimInstance>]
    [[-User] <String>]
    [[-Password] <String>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# Principal parameter set
Set-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [[-Action] <CimInstance[]>]
    [[-Trigger] <CimInstance[]>]
    [[-Settings] <CimInstance>]
    [[-Principal] <CimInstance>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# Object parameter set
Set-ScheduledTask
    [-InputObject] <CimInstance>
    [[-User] <String>]
    [[-Password] <String>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-TaskName` | String | Yes (User/Principal) | Name of the task to modify. |
| `-TaskPath` | String | No | Path in namespace. |
| `-Action` | CimInstance[] | No | Replacement action objects. |
| `-Trigger` | CimInstance[] | No | Replacement trigger objects. |
| `-Settings` | CimInstance | No | Replacement settings object. |
| `-Principal` | CimInstance | No (Principal) | Replacement principal object. |
| `-User` | String | No (User/Object) | Replacement run-as user account. |
| `-Password` | String | No | Password for the user account. |
| `-InputObject` | CimInstance | Yes (Object) | Task object from pipeline. |

### Output

`CimInstance` (MSFT_ScheduledTask)

### Examples

```powershell
# Modify a task trigger
$Time = New-ScheduledTaskTrigger -At 12:00 -Once
Set-ScheduledTask -TaskName "SoftwareScan" -Trigger $Time

# Replace actions on a task
$Act1 = New-ScheduledTaskAction -Execute "Notepad.exe"
$Act2 = New-ScheduledTaskAction -Execute "Calc.exe"
Set-ScheduledTask "DeployTools" -Action $Act1,$Act2

# Change the run-as user (requires password)
Set-ScheduledTask -TaskName "MyTask" -User "DOMAIN\newuser" -Password "P@ssw0rd"

# Update settings
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 1) -RestartCount 3
Set-ScheduledTask -TaskName "MyTask" -Settings $settings

# Update both trigger and action
$trigger = New-ScheduledTaskTrigger -Daily -At "3:00 AM"
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-File C:\script.ps1"
Set-ScheduledTask -TaskName "MyTask" -Trigger $trigger -Action $action

# Modify via pipeline
Get-ScheduledTask -TaskName "MyTask" | Set-ScheduledTask -Settings (New-ScheduledTaskSettingsSet -StartWhenAvailable)

# Change principal using Principal object
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
Set-ScheduledTask -TaskName "MyTask" -Principal $principal
```

---

## 6. Unregister-ScheduledTask

Removes (deletes) a scheduled task from Task Scheduler.

### Syntax

```powershell
# By name/path
Unregister-ScheduledTask
    [[-TaskName] <String[]>]
    [[-TaskPath] <String[]>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
    [-PassThru]
    [-WhatIf]
    [-Confirm]

# By input object
Unregister-ScheduledTask
    -InputObject <CimInstance[]>
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
    [-PassThru]
    [-WhatIf]
    [-Confirm]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-TaskName` | String[] | No (ByPath) | One or more task names. Accepts pipeline input by property name. |
| `-TaskPath` | String[] | No (ByPath) | One or more task paths. |
| `-InputObject` | CimInstance[] | Yes (Object) | Task object(s) from pipeline. |
| `-PassThru` | SwitchParameter | No | Returns the unregistered task object. |
| `-WhatIf` | SwitchParameter | No | Shows what would happen without executing. |
| `-Confirm` | SwitchParameter | No | Prompts for confirmation before deleting (default: True). |
| `-CimSession` | CimSession[] | No | Remote session. |
| `-ThrottleLimit` | Int32 | No | Max concurrent operations. |
| `-AsJob` | SwitchParameter | No | Run as background job. |

### Examples

```powershell
# Delete a task (will prompt for confirmation)
Unregister-ScheduledTask -TaskName "HardwareInventory"

# Delete without confirmation
Unregister-ScheduledTask -TaskName "HardwareInventory" -Confirm:$false

# Delete from a specific path
Unregister-ScheduledTask -TaskPath '\Event Viewer Tasks\' -TaskName 'ForwardedEvents' -Confirm:$false

# Preview what would be deleted
Unregister-ScheduledTask -TaskName "OldTask" -WhatIf

# Delete via pipeline
Get-ScheduledTask -TaskPath "\Temp\" | Unregister-ScheduledTask -Confirm:$false

# Delete and return the object
Unregister-ScheduledTask -TaskName "MyTask" -PassThru

# Bulk delete disabled tasks
Get-ScheduledTask | Where-Object {$_.State -eq "Disabled"} | Unregister-ScheduledTask -Confirm:$false
```

---

## 7. Enable-ScheduledTask

Enables a disabled scheduled task.

### Syntax

```powershell
# By name
Enable-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# By object
Enable-ScheduledTask
    [-InputObject] <CimInstance>
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-TaskName` | String | Yes (Name) | Name of the task to enable. |
| `-TaskPath` | String | No (Name) | Path in namespace. |
| `-InputObject` | CimInstance | Yes (Object) | Task object from pipeline. |

### Output

`CimInstance` (MSFT_ScheduledTask) -- returns the task with updated State.

### Examples

```powershell
# Enable a specific task
Enable-ScheduledTask -TaskName "SystemScan"

# Enable all tasks in a folder
Get-ScheduledTask -TaskPath "\UpdateTasks\" | Enable-ScheduledTask

# Enable all disabled tasks
Get-ScheduledTask | Where-Object {$_.State -eq "Disabled"} | Enable-ScheduledTask

# Enable and verify
Enable-ScheduledTask -TaskName "MyTask" | Select-Object TaskName, State
```

---

## 8. Disable-ScheduledTask

Disables a scheduled task (prevents it from running on its trigger).

### Syntax

```powershell
# By name
Disable-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# By object
Disable-ScheduledTask
    [-InputObject] <CimInstance>
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

Same structure as Enable-ScheduledTask.

### Output

`CimInstance` (MSFT_ScheduledTask)

### Examples

```powershell
# Disable a specific task
Disable-ScheduledTask -TaskName "SystemScan"

# Disable all tasks in a folder
Get-ScheduledTask -TaskPath "\UpdateTasks\" | Disable-ScheduledTask

# Disable all tasks matching a pattern
Get-ScheduledTask -TaskPath "\Microsoft\Windows\Defrag\" | Disable-ScheduledTask

# Disable and verify
Disable-ScheduledTask -TaskName "MyTask" | Select-Object TaskName, State
# State should show "Disabled"
```

---

## 9. Start-ScheduledTask

Manually starts one or more instances of a registered task (immediately, asynchronously).

### Syntax

```powershell
# By name/path
Start-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# By object
Start-ScheduledTask
    [-InputObject] <CimInstance>
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-TaskName` | String | Yes (Path) | Name of the task to start. |
| `-TaskPath` | String | No (Path) | Path in namespace. |
| `-InputObject` | CimInstance | Yes (Object) | Task object from pipeline. |

### Output

None (void).

### Examples

```powershell
# Start a task immediately
Start-ScheduledTask -TaskName "ScanSoftware"

# Start all tasks in a folder
Get-ScheduledTask -TaskPath "\UpdateTasks\UpdateVirus\" | Start-ScheduledTask

# Start a task in a specific path
Start-ScheduledTask -TaskName "Cleanup" -TaskPath "\MyTasks\"

# Start and wait, then check result
Start-ScheduledTask -TaskName "MyTask"
Start-Sleep -Seconds 10
Get-ScheduledTaskInfo -TaskName "MyTask" | Select-Object LastRunTime, LastTaskResult
```

---

## 10. Stop-ScheduledTask

Immediately stops all running instances of a task.

### Syntax

```powershell
# By name/path
Stop-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# By object
Stop-ScheduledTask
    [-InputObject] <CimInstance>
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-TaskName` | String | Yes (Path) | Name of the task to stop. |
| `-TaskPath` | String | No (Path) | Path in namespace. |
| `-InputObject` | CimInstance | Yes (Object) | Task object from pipeline. |

### Output

`CimInstance` (MSFT_ScheduledTask) -- returns the registered task object.

### Examples

```powershell
# Stop a specific task
Stop-ScheduledTask -TaskName "ScanSoftware"

# Stop all tasks in a folder
Get-ScheduledTask -TaskPath "\UpdateTasks\" | Stop-ScheduledTask

# Stop a task in a specific path
Stop-ScheduledTask -TaskName "LongRunningJob" -TaskPath "\MyTasks\"

# Stop all currently running tasks
Get-ScheduledTask | Where-Object {$_.State -eq "Running"} | Stop-ScheduledTask
```

---

## 11. Export-ScheduledTask

Exports a scheduled task definition as an XML string.

### Syntax

```powershell
# By name
Export-ScheduledTask
    [[-TaskName] <String>]
    [[-TaskPath] <String>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# By object
Export-ScheduledTask
    [-InputObject] <CimInstance>
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-TaskName` | String | No (Name) | Name of the task to export. |
| `-TaskPath` | String | No (Name) | Path in namespace. |
| `-InputObject` | CimInstance | Yes (Object) | Task object from pipeline. |

### Output

`String` -- XML definition of the task.

### Examples

```powershell
# Export a task as XML
Export-ScheduledTask -TaskName "UpdateDrivers" -TaskPath "\UpdateTasks\"

# Export all tasks in a folder
Get-ScheduledTask -TaskPath "\UpdateTasks\" | Export-ScheduledTask

# Save task XML to file
$xml = Export-ScheduledTask -TaskName "MyTask"
$xml | Out-File -FilePath "C:\Backups\MyTask.xml" -Encoding UTF8

# Export all tasks to individual files
Get-ScheduledTask | ForEach-Object {
    $xml = Export-ScheduledTask -InputObject $_
    $path = "C:\TaskBackups\$($_.TaskName -replace '[\\\/:]', '_').xml"
    $xml | Out-File -FilePath $path -Encoding UTF8
}

# Copy a task: export then re-register with new name
$xml = Export-ScheduledTask -TaskName "OriginalTask"
Register-ScheduledTask -TaskName "CopiedTask" -Xml $xml -Force

# Export for use on another machine (combine with XML import)
Export-ScheduledTask -TaskName "DeployTask" -TaskPath "\Custom\" | Out-File "\\server\share\DeployTask.xml"
```

---

## Import-ScheduledTask

**NOTE:** `Import-ScheduledTask` does NOT exist as a standalone cmdlet in the ScheduledTasks module. To import a task from XML, use `Register-ScheduledTask -Xml`:

```powershell
# Import from XML file
$xml = Get-Content -Path "C:\Backups\MyTask.xml" -Raw
Register-ScheduledTask -TaskName "ImportedTask" -Xml $xml

# Import and overwrite existing
$xml = Get-Content -Path "C:\Backups\MyTask.xml" -Raw
Register-ScheduledTask -TaskName "ImportedTask" -Xml $xml -Force

# Import from a remote share
$xml = Get-Content -Path "\\server\share\MyTask.xml" -Raw
Register-ScheduledTask -TaskName "RemoteImport" -Xml $xml -Force

# Round-trip: export and re-import on another machine
# On source machine:
Export-ScheduledTask -TaskName "MyTask" | Out-File "\\share\MyTask.xml"
# On destination machine:
$xml = Get-Content "\\share\MyTask.xml" -Raw
Register-ScheduledTask -TaskName "MyTask" -Xml $xml
```

---

## 12. New-ScheduledTaskTrigger

Creates a trigger object that defines when a task starts. Supports 5 parameter sets for different trigger types.

### Parameter Sets and Syntax

#### Once Trigger
```powershell
New-ScheduledTaskTrigger
    -At <DateTime>
    [-Once]
    [-RandomDelay <TimeSpan>]
    [-RepetitionInterval <TimeSpan>]
    [-RepetitionDuration <TimeSpan>]
```

#### Daily Trigger
```powershell
New-ScheduledTaskTrigger
    -At <DateTime>
    [-Daily]
    [-DaysInterval <UInt32>]
    [-RandomDelay <TimeSpan>]
```

#### Weekly Trigger
```powershell
New-ScheduledTaskTrigger
    -At <DateTime>
    [-Weekly]
    [-DaysOfWeek <DayOfWeek[]>]
    [-WeeksInterval <UInt32>]
    [-RandomDelay <TimeSpan>]
```

#### Startup Trigger
```powershell
New-ScheduledTaskTrigger
    [-AtStartup]
    [-RandomDelay <TimeSpan>]
```

#### Logon Trigger
```powershell
New-ScheduledTaskTrigger
    [-AtLogOn]
    [-User <String>]
    [-RandomDelay <TimeSpan>]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-At` | DateTime | Yes (Once/Daily/Weekly) | Date/time to trigger the task. |
| `-Once` | SwitchParameter | Yes | One-time trigger at the `-At` time. |
| `-Daily` | SwitchParameter | Yes | Recurring daily schedule. |
| `-Weekly` | SwitchParameter | Yes | Recurring weekly schedule. |
| `-AtStartup` | SwitchParameter | Yes | Trigger on system startup. |
| `-AtLogOn` | SwitchParameter | Yes | Trigger on user logon. |
| `-DaysInterval` | UInt32 | No (Daily) | Days between runs. 1 = every day, 2 = every other day, etc. |
| `-DaysOfWeek` | DayOfWeek[] | No (Weekly) | Days: Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday. |
| `-WeeksInterval` | UInt32 | No (Weekly) | Weeks between runs. 1 = every week, 2 = every other week. |
| `-User` | String | No (Logon) | Specific user for logon trigger. If omitted, any user logon triggers. |
| `-RandomDelay` | TimeSpan | No | Random delay before trigger fires (applies to all types). |
| `-RepetitionInterval` | TimeSpan | No (Once) | Time between repetitions (e.g., run every 30 min). |
| `-RepetitionDuration` | TimeSpan | No (Once) | How long the repetition pattern continues. |

### Output

`CimInstance` (MSFT_TaskTrigger)

### Examples

```powershell
# One-time trigger: run once at a specific date/time
New-ScheduledTaskTrigger -Once -At "2026-09-15T02:00:00"

# One-time with repetition: run every 30 minutes for 4 hours
New-ScheduledTaskTrigger -Once -At (Get-Date).Date.AddDays(1) `
    -RepetitionInterval (New-TimeSpan -Minutes 30) `
    -RepetitionDuration (New-TimeSpan -Hours 4)

# One-time with indefinite repetition (repeat until manually stopped)
New-ScheduledTaskTrigger -Once -At "3am" `
    -RepetitionInterval (New-TimeSpan -Hours 1) `
    -RepetitionDuration ([TimeSpan]::MaxValue)

# Daily trigger: every day at 3 AM
New-ScheduledTaskTrigger -Daily -At "3am"

# Daily every 3 days at 8 AM
New-ScheduledTaskTrigger -Daily -DaysInterval 3 -At "8:00 AM"

# Weekly trigger: every Monday and Wednesday at 9 AM
New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday,Wednesday -At "9:00 AM"

# Weekly every other week on Sunday at 3 AM
New-ScheduledTaskTrigger -Weekly -WeeksInterval 2 -DaysOfWeek Sunday -At "3am"

# Startup trigger: run when the system boots
New-ScheduledTaskTrigger -AtStartup

# Startup with random delay up to 5 minutes
New-ScheduledTaskTrigger -AtStartup -RandomDelay (New-TimeSpan -Minutes 5)

# Logon trigger: run when any user logs on
New-ScheduledTaskTrigger -AtLogOn

# Logon trigger: specific user
New-ScheduledTaskTrigger -AtLogOn -User "DOMAIN\jsmith"

# Logon trigger with random delay
New-ScheduledTaskTrigger -AtLogOn -RandomDelay (New-TimeSpan -Minutes 10)

# Using DateTime objects for precise scheduling
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date -Date "2026-09-01T09:00:00")

# Multiple triggers on one task (task runs on EITHER trigger)
$trigger1 = New-ScheduledTaskTrigger -Daily -At "8:00 AM"
$trigger2 = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Saturday -At "10:00 AM"
Register-ScheduledTask -TaskName "MultiTrigger" -Action $action -Trigger $trigger1,$trigger2
```

---

## 13. New-ScheduledTaskAction

Creates an action object defining what the task executes.

### Syntax

```powershell
New-ScheduledTaskAction
    [-Execute] <String>
    [[-Argument] <String>]
    [[-WorkingDirectory] <String>]
    [-Id <String>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-Execute` | String | Yes | Path to the executable file (.exe, .com, .bat, .cmd, .ps1, etc.). |
| `-Argument` | String | No | Command-line arguments passed to the executable. |
| `-WorkingDirectory` | String | No | Directory in which the task runs. Default: `%windir%\system32`. |
| `-Id` | String | No | Identifier for the action (used for logging). |
| `-CimSession` | CimSession[] | No | Remote session. |
| `-ThrottleLimit` | Int32 | No | Max concurrent operations. |
| `-AsJob` | SwitchParameter | No | Run as background job. |

### Output

`CimInstance` (MSFT_TaskAction)

### Examples

```powershell
# Simple executable
New-ScheduledTaskAction -Execute "PowerShell.exe"

# PowerShell with arguments
New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-File C:\Scripts\backup.ps1 -Verbose"

# PowerShell with command and working directory
New-ScheduledTaskAction -Execute "PowerShell.exe" `
    -Argument "-ExecutionPolicy Bypass -File C:\Scripts\task.ps1" `
    -WorkingDirectory "C:\Scripts"

# Run a batch file
New-ScheduledTaskAction -Execute "C:\Scripts\cleanup.bat"

# Run a scheduled task (action = start another task)
New-ScheduledTaskAction -Execute "schtasks.exe" -Argument "/Run /TN ""\MyTasks\OtherTask"""

# Run an executable with environment-friendly path
New-ScheduledTaskAction -Execute "%ProgramFiles%\7-Zip\7z.exe" -Argument "a C:\Backup\archive.7z C:\Data\*"

# With an ID for logging
New-ScheduledTaskAction -Execute "notepad.exe" -Id "OpenNotepad"

# Multiple actions on one task (run sequentially)
$action1 = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-File C:\scripts\step1.ps1"
$action2 = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-File C:\scripts\step2.ps1"
$action3 = New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/C echo Done >> C:\log.txt"
Register-ScheduledTask -TaskName "MultiStep" -Action $action1,$action2,$action3

# Action with complex arguments
$action = New-ScheduledTaskAction `
    -Execute "robocopy.exe" `
    -Argument "C:\Source D:\Backup /MIR /R:3 /W:5 /LOG:C:\Logs\robocopy.log"
```

---

## 14. New-ScheduledTaskPrincipal

Creates a principal object defining the security context (user, group, logon type, privileges).

### Syntax

```powershell
# User parameter set (default)
New-ScheduledTaskPrincipal
    [-UserId] <String>
    [[-Id] <String>]
    [[-LogonType] <LogonTypeEnum>]
    [[-RunLevel] <RunLevelEnum>]
    [[-ProcessTokenSidType] <ProcessTokenSidTypeEnum>]
    [[-RequiredPrivilege] <String[]>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]

# Group parameter set
New-ScheduledTaskPrincipal
    [-GroupId] <String>
    [[-Id] <String>]
    [[-RunLevel] <RunLevelEnum>]
    [[-ProcessTokenSidType] <ProcessTokenSidTypeEnum>]
    [[-RequiredPrivilege] <String[]>]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `-UserId` | String | Yes (User) | User account under which the task runs. |
| `-GroupId` | String | Yes (Group) | User group under which the task runs. |
| `-Id` | String | No | Identifier for the principal. |
| `-LogonType` | LogonTypeEnum | No (User) | Security logon method. See values below. |
| `-RunLevel` | RunLevelEnum | No | Privilege level. `Limited` (LUA) or `Highest` (full admin). |
| `-ProcessTokenSidType` | ProcessTokenSidTypeEnum | No | SID type: `None`, `Unrestricted`, `Default`. |
| `-RequiredPrivilege` | String[] | No | Array of user right constants the task requires. |

### LogonType Values

| Value | Description |
|-------|-------------|
| `None` | No logon type specified. |
| `Password` | Task runs with stored password. Credentials saved. |
| `S4U` | Service-for-User. No password stored; runs in user context if logged on. |
| `Interactive` | Task runs only when user is logged on interactively. |
| `Group` | Task runs as members of a group. |
| `ServiceAccount` | Task runs as a service account (SYSTEM, LOCAL SERVICE, NETWORK SERVICE). |
| `InteractiveOrPassword` | Interactive or password-based logon. |

### RunLevel Values

| Value | Description |
|-------|-------------|
| `Limited` | Runs with least-privilege user account (LUA). Default. |
| `Highest` | Runs with highest privileges (elevated). |

### Output

`CimInstance` (MSFT_TaskPrincipal)

### Examples

```powershell
# Run as SYSTEM (service account, highest privileges)
New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\SYSTEM" -LogonType ServiceAccount -RunLevel Highest

# Run as LOCAL SERVICE
New-ScheduledTaskPrincipal -UserId "LOCALSERVICE" -LogonType ServiceAccount

# Run as NETWORK SERVICE
New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\NETWORKSERVICE" -LogonType ServiceAccount

# Run as a specific user (requires password at registration)
New-ScheduledTaskPrincipal -UserId "DOMAIN\jsmith" -LogonType Password -RunLevel Limited

# Run as current user without storing password (S4U)
New-ScheduledTaskPrincipal -UserId "$env:USERNAME" -LogonType S4U

# Run as a group (any logged-in member of Administrators)
New-ScheduledTaskPrincipal -GroupId "BUILTIN\Administrators" -RunLevel Highest

# Interactive only (only when user is logged on)
New-ScheduledTaskPrincipal -UserId "$env:USERNAME" -LogonType Interactive

# With specific ID
New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest -Id "SystemPrincipal"

# With required privileges
New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount `
    -RunLevel Highest `
    -RequiredPrivilege @("SeIncreaseQuotaPrivilege", "SeAssignPrimaryTokenPrivilege")

# Full example: register a task with principal
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-File C:\Scripts\deploy.ps1"
$trigger = New-ScheduledTaskTrigger -Daily -At "3:00 AM"
Register-ScheduledTask -TaskName "DeployService" -Action $action -Trigger $trigger -Principal $principal
```

---

## 15. New-ScheduledTaskSettingsSet

Creates a settings object controlling Task Scheduler behavior for execution limits, restarts, idle conditions, power management, priorities, and more.

### Syntax

```powershell
New-ScheduledTaskSettingsSet
    [-DisallowDemandStart]
    [-DisallowHardTerminate]
    [-Compatibility <CompatibilityEnum>]
    [-DeleteExpiredTaskAfter <TimeSpan>]
    [-AllowStartIfOnBatteries]
    [-Disable]
    [-MaintenanceExclusive]
    [-Hidden]
    [-RunOnlyIfIdle]
    [-IdleWaitTimeout <TimeSpan>]
    [-IdleDuration <TimeSpan>]
    [-NetworkId <String>]
    [-NetworkName <String>]
    [-DisallowStartOnRemoteAppSession]
    [-MaintenancePeriod <TimeSpan>]
    [-MaintenanceDeadline <TimeSpan>]
    [-StartWhenAvailable]
    [-DontStopIfGoingOnBatteries]
    [-WakeToRun]
    [-RestartOnIdle]
    [-DontStopOnIdleEnd]
    [-ExecutionTimeLimit <TimeSpan>]
    [-MultipleInstances <MultipleInstancesEnum>]
    [-Priority <Int32>]
    [-RestartCount <Int32>]
    [-RestartInterval <TimeSpan>]
    [-RunOnlyIfNetworkAvailable]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-AsJob]
```

### Complete Parameter Reference

| Parameter | Type | Description |
|-----------|------|-------------|
| **Execution Limits** | | |
| `-ExecutionTimeLimit` | TimeSpan | Max time allowed for task to complete. Default: 3 days. Use `New-TimeSpan` or `[TimeSpan]` to set. |
| `-DeleteExpiredTaskAfter` | TimeSpan | Time to wait before deleting an expired task. |
| **Restart Behavior** | | |
| `-RestartCount` | Int32 | Number of times Task Scheduler attempts to restart the task after failure. |
| `-RestartInterval` | TimeSpan | Time between restart attempts. Must be >= 1 minute. |
| **Priority** | | |
| `-Priority` | Int32 | 0 (highest) to 10 (lowest). Default: 7. Levels 4-6 = interactive, 7-8 = background. |
| **Idle Conditions** | | |
| `-RunOnlyIfIdle` | SwitchParameter | Run only when the computer is idle. |
| `-IdleDuration` | TimeSpan | How long the computer must be idle before task runs. |
| `-IdleWaitTimeout` | TimeSpan | How long to wait for an idle condition before giving up. |
| `-DontStopOnIdleEnd` | SwitchParameter | Do not terminate if idle condition ends. |
| `-RestartOnIdle` | SwitchParameter | Restart task when computer cycles into idle. |
| **Power Management** | | |
| `-AllowStartIfOnBatteries` | SwitchParameter | Start task even on battery power. |
| `-DontStopIfGoingOnBatteries` | SwitchParameter | Do not stop if switching to battery. |
| `-WakeToRun` | SwitchParameter | Wake computer from sleep/hibernate to run the task. |
| **Network Conditions** | | |
| `-RunOnlyIfNetworkAvailable` | SwitchParameter | Run only when a network is available. |
| `-NetworkId` | String | Network profile ID for network-aware tasks. |
| `-NetworkName` | String | Network profile name (display purposes). |
| **Start Behavior** | | |
| `-StartWhenAvailable` | SwitchParameter | Start task if a scheduled time was missed. |
| `-DisallowDemandStart` | SwitchParameter | Prevent manual (Run/context menu) start. |
| `-DisallowStartOnRemoteAppSession` | SwitchParameter | Do not start in a RemoteApp (RAIL) session. |
| **Termination** | | |
| `-DisallowHardTerminate` | SwitchParameter | Prevent TerminateProcess from killing the task. |
| **Multiple Instances** | | |
| `-MultipleInstances` | MultipleInstancesEnum | `IgnoreNew` (default), `Parallel` (start immediately), `Queue` (wait for current to finish). |
| **Display** | | |
| `-Hidden` | SwitchParameter | Hide task from Task Scheduler UI. |
| `-Disable` | SwitchParameter | Create the task in a disabled state. |
| **Compatibility** | | |
| `-Compatibility` | CompatibilityEnum | `At`, `V1`, `Vista`, `Win7`, `Win8`. |
| **Maintenance** | | |
| `-MaintenancePeriod` | TimeSpan | Time window for regular Automatic maintenance. |
| `-MaintenanceDeadline` | TimeSpan | Deadline for emergency maintenance. |
| `-MaintenanceExclusive` | SwitchParameter | Task runs alone during maintenance mode. |

### Output

`CimInstance` (MSFT_TaskSettings)

### Examples

```powershell
# Default settings
New-ScheduledTaskSettingsSet

# Set execution time limit to 1 hour
New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 1)

# No execution time limit (remove the default 3-day limit)
New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero)

# Restart on failure: try 3 times, 10 minutes apart
New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 10)

# Idle settings: only run after 2 minutes idle, wait up to 2.5 hours
New-ScheduledTaskSettingsSet -RunOnlyIfIdle -IdleDuration (New-TimeSpan -Minutes 2) -IdleWaitTimeout (New-TimeSpan -Hours 2 -Minutes 30)

# Power-aware: laptop-friendly
New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -WakeToRun

# Network-aware: only run when connected
New-ScheduledTaskSettingsSet -RunOnlyIfNetworkAvailable

# High priority, hidden, start if missed
New-ScheduledTaskSettingsSet -Priority 4 -Hidden -StartWhenAvailable

# Create disabled task with unlimited execution time
New-ScheduledTaskSettingsSet -Disable -ExecutionTimeLimit ([TimeSpan]::Zero)

# Queue multiple instances instead of ignoring
New-ScheduledTaskSettingsSet -MultipleInstances Queue

# Complete enterprise task settings
New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit (New-TimeSpan -Hours 4) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 15) `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -RunOnlyIfNetworkAvailable `
    -Priority 5 `
    -Hidden

# Custom priority (interactive-level)
New-ScheduledTaskSettingsSet -Priority 5

# Full registration with settings
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-File C:\Scripts\maintenance.ps1"
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At "2:00 AM"
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 30) `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -RunOnlyIfNetworkAvailable `
    -Hidden
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName "WeeklyMaintenance" `
    -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description "Weekly system maintenance task"
```

---

## Quick Reference: Common Task States

| State | Meaning |
|-------|---------|
| `Ready` | Task is enabled and waiting for its trigger. |
| `Running` | Task is currently executing. |
| `Disabled` | Task is disabled and will not run. |
| `Queued` | Task is waiting for a previous instance to finish (MultipleInstances = Queue). |

## Quick Reference: Common LastTaskResult Codes

| Code | Meaning |
|------|---------|
| `0` | Success (S_OK) |
| `1` | Incorrect function |
| `2` | File not found |
| `10` | The environment is incorrect |
| `267009` | Task has not yet run |
| `267011` | Task is not yet running |
| `267014` | Task is currently running |
| `2147942401` (0x80070001) | Incorrect function |
| `2147942402` (0x80070002) | File not found |
| `2147942683` (0x8007042B) | Process terminated unexpectedly |

## Quick Reference: Common RequiredPrivilege Constants

| Constant | Description |
|----------|-------------|
| `SeIncreaseQuotaPrivilege` | Adjust memory quotas for a process |
| `SeAssignPrimaryTokenPrivilege` | Replace a process level token |
| `SeTcbPrivilege` | Act as part of the operating system |
| `SeCreateTokenPrivilege` | Create a token object |
| `SeBackupPrivilege` | Back up files and directories |
| `SeRestorePrivilege` | Restore files and directories |
| `SeTakeOwnershipPrivilege` | Take ownership of files/objects |
| `SeLoadDriverPrivilege` | Load and unload device drivers |
| `SeSystemtimePrivilege` | Change the system time |
| `SeCreateGlobalPrivilege` | Create global objects |

---

*Sources: Official Microsoft documentation at https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/*
