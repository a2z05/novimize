# Windows Scheduled Tasks - Definitive Reference

Comprehensive reference covering the Task Scheduler API, PowerShell cmdlets,
schtasks.exe, triggers, conditions, common system tasks, and task XML format.

---

## Table of Contents

1. [Task Scheduler API](#1-task-scheduler-api)
2. [PowerShell ScheduledTasks Cmdlets](#2-powershell-scheduledtasks-cmdlets)
3. [schtasks.exe Command-Line Tool](#3-schtasksxexe-command-line-tool)
4. [Trigger Types](#4-trigger-types)
5. [Task Conditions](#5-task-conditions)
6. [Common System Tasks - Safety Assessment](#6-common-system-tasks---safety-assessment)
7. [Task XML Format](#7-task-xml-format)

---

## 1. Task Scheduler API

### 1.1 Architecture Overview

Windows Task Scheduler (since Vista/Server 2008) uses the Task Scheduler 2.0
engine, which exposes COM interfaces for programmatic control. Tasks are stored
as XML files in `%SystemRoot%\System32\Tasks\` and indexed in the registry under
`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\`.

### 1.2 Core COM Interfaces

| Interface | CLSID / Header | Purpose |
|-----------|---------------|---------|
| ITaskService | Taskschd.h | Entry point. Connects to local or remote scheduler, provides access to root task folder. |
| ITaskFolder | Taskschd.h | Represents a folder in the task tree. Enumerates subfolders and tasks, registers/creates tasks. |
| ITask | Taskschd.h | Represents a single scheduled task. Read/write properties: name, path, state, enabled, last/next run time. |
| ITaskDefinition | Taskschd.h | Full task definition: triggers, actions, principals, settings, data, and XML. |
| ITrigger | Taskschd.h | Base interface for all trigger types. Properties: start boundary, end boundary, repetition, enabled. |
| IAction | Taskschd.h | Base interface for all action types (Exec, ComHandler, SendEmail, ShowMessage). |
| IPrincipal | Taskschd.h | Security context: user, group, logon type, required privilege level. |
| ITaskSettings | Taskschd.h | Execution settings: priority, time limit, restart policy, multiple instances policy, idle settings. |
| IRegistrationInfo | Taskschd.h | Metadata: description, author, date, URI, version, documentation link. |
| ITaskCollection | Taskschd.h | Enumeration of tasks or triggers or actions within a folder or definition. |
| IRunningTaskCollection | Taskschd.h | Currently executing task instances. |
| IRegisteredTask | Taskschd.h | Registered task handle returned from folder enumeration; wraps ITask with run/stop/getInstances. |

### 1.3 COM Usage Pattern (C++)

```cpp
#include <taskschd.h>
#include <comdef.h>

#pragma comment(lib, "taskschd.lib")

HRESULT ManageTask() {
    HRESULT hr = CoInitializeEx(NULL, COINIT_MULTITHREADED);
    if (FAILED(hr)) return hr;

    ITaskService* pService = nullptr;
    hr = CoCreateInstance(
        CLSID_TaskScheduler, NULL, CLSCTX_INPROC_SERVER,
        IID_ITaskService, (void**)&pService);
    if (FAILED(hr)) goto cleanup;

    hr = pService->Connect(_variant_t(), _variant_t(), _variant_t(), _variant_t());
    if (FAILED(hr)) goto cleanup;

    ITaskFolder* pRootFolder = nullptr;
    hr = pService->GetFolder(_bstr_t(L"\\"), &pRootFolder);
    if (FAILED(hr)) goto cleanup;

    // Enumerate tasks in root folder
    IRegisteredTaskCollection* pTaskCollection = nullptr;
    hr = pRootFolder->GetTasks(TASK_ENUM_HIDDEN, &pTaskCollection);
    if (SUCCEEDED(hr)) {
        LONG count = 0;
        pTaskCollection->get_Count(&count);
        for (LONG i = 0; i < count; i++) {
            IRegisteredTask* pTask = nullptr;
            pTaskCollection->get_Item(_variant_t(i + 1), &pTask);
            if (pTask) {
                BSTR taskName = nullptr;
                pTask->get_Name(&taskName);
                // Process task...
                SysFreeString(taskName);
                pTask->Release();
            }
        }
        pTaskCollection->Release();
    }

cleanup:
    if (pRootFolder) pRootFolder->Release();
    if (pService) pService->Release();
    CoUninitialize();
    return hr;
}
```

### 1.4 COM Usage Pattern (C# / .NET)

```csharp
using Microsoft.Win32.TaskScheduler;

// Connect to local scheduler
using (TaskService ts = new TaskService()) {
    // Enumerate all tasks
    foreach (Task task in ts.AllTasks) {
        Console.WriteLine($"{task.Path} | State: {task.State} | Enabled: {task.Enabled}");
    }

    // Create a new task
    TaskDefinition td = ts.NewTask();
    td.RegistrationInfo.Description = "My custom task";

    td.Triggers.Add(new DailyTrigger { StartBoundary = DateTime.Today.AddHours(9) });
    td.Actions.Add(new ExecAction("notepad.exe", "C:\\log.txt", null));
    td.Settings.DisallowStartIfOnBatteries = false;
    td.Settings.StopIfGoingOnBatteries = false;

    // Register (create) the task
    ts.RootFolder.RegisterTaskDefinition(
        "MyTask", td, TaskCreation.CreateOrUpdate,
        "SYSTEM", null, TaskLogonType.ServiceAccount);
}
```

### 1.5 NuGet Packages

| Package | ID | Notes |
|---------|----|-------|
| TaskScheduler (ntaoo) | `TaskScheduler` | Wrapper around COM; rich API; supports .NET 4.0+. Most popular managed wrapper. |
| Microsoft.Win32.TaskScheduler | Same name, different author historically | Verify package ID on NuGet.org; the ntaoo package is the canonical one. |

Install:

```powershell
Install-Package TaskScheduler -Source nuget.org
```

### 1.6 PowerShell ScheduledTasks Module

Available on Windows 8+ / Server 2012+. Module path:
`%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules\ScheduledTasks\ScheduledTasks.psd1`

Module includes 15 exported cmdlets (covered in Section 2).

### 1.7 schtasks.exe

Legacy command-line utility, available on all supported Windows versions.
Covered in detail in Section 3.

---

## 2. PowerShell ScheduledTasks Cmdlets

### 2.1 Get-ScheduledTask

Retrieves registered scheduled tasks.

```powershell
Get-ScheduledTask
    [-TaskName <String[]>]
    [-TaskPath <String[]>]
    [-AsJob]
    [<CommonParameters>]

Get-ScheduledTask
    [-AsJob]
    [-CimSession <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [-Query <String>]
    [-Primitive <String>]
    [<CommonParameters>]
```

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| TaskName | String[] | One or more task names. Supports wildcards (*, ?). |
| TaskPath | String[] | Folder path (e.g., `\Microsoft\Windows\Defrag\`). Must start and end with `\`. |
| Query | String | Filter using XPath query against the task XML. |
| CimSession | CimSession[] | Remote computer(s) via CIM session. |
| AsJob | Switch | Run as background job. |
| ThrottleLimit | Int32 | Max concurrent CIM operations (default: 32). |

**Examples:**

```powershell
# All tasks
Get-ScheduledTask

# Tasks in a specific folder
Get-ScheduledTask -TaskPath "\Microsoft\Windows\Defrag\"

# By name with wildcard
Get-ScheduledTask -TaskName "*Update*"

# Only enabled tasks
Get-ScheduledTask | Where-Object {$_.State -ne 'Disabled'}

# Export task as XML
Get-ScheduledTask -TaskName "\MyTask" | Export-ScheduledTask -Path "C:\exported.xml"
```

**Output properties:** TaskName, TaskPath, State (Ready, Running, Disabled),
TaskInfo (StateInfo), Actions, Triggers, Principal, Settings, RegistrationInfo.

### 2.2 Get-ScheduledTaskInfo

Retrieves history and status information for a scheduled task.

```powershell
Get-ScheduledTaskInfo
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [<CommonParameters>]
```

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| TaskName | String | Name of the task (required, pipeline by value). |
| TaskPath | String | Folder path (optional, defaults to `\`). |

**Output properties:** LastRunTime, LastTaskResult, NextRunTime,
NumberOfMissedRuns, NumberOfMissedRunsAfterRestart.

**Examples:**

```powershell
# Get info for a specific task
Get-ScheduledTaskInfo -TaskName "\Microsoft\Windows\Defrag\ScheduledDefrag"

# Pipe from Get-ScheduledTask
Get-ScheduledTask -TaskName "*Defrag*" | Get-ScheduledTaskInfo

# List tasks that failed on last run
Get-ScheduledTask | Get-ScheduledTaskInfo | Where-Object {$_.LastTaskResult -ne 0 -and $_.LastRunTime -ne [datetime]::MinValue}
```

### 2.3 New-ScheduledTask

Creates an in-memory task definition object (does NOT register it).

```powershell
New-ScheduledTask
    [[-Action] <CimInstance[]>]
    [[-Trigger] <CimInstance[]>]
    [[-Settings] <CimInstance>]
    [[-Principal] <CimInstance>]
    [[-Description] <String>]
    [[-Author] <String>]
    [[-URI] <String>]
    [[-SecurityDescriptor] <String>]
    [[-SourceVersion] <String>]
    [[-SourceType] <String>]
    [<CommonParameters>]
```

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| Action | CimInstance[] | Action(s) to perform (from New-ScheduledTaskAction). |
| Trigger | CimInstance[] | Trigger(s) for the task (from New-ScheduledTaskTrigger). |
| Settings | CimInstance | Execution settings (from New-ScheduledTaskSettingsSet). |
| Principal | CimInstance | Security context (from New-ScheduledTaskPrincipal). |
| Description | String | Human-readable task description. |
| Author | String | Author name (defaults to current user). |
| URI | String | Unique identifier URI for the task. |
| SecurityDescriptor | String | SDDL string for task permissions. |

**Example:**

```powershell
$action  = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-File C:\Scripts\backup.ps1"
$trigger = New-ScheduledTaskTrigger -Daily -At "2:00AM"
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest

$task = New-ScheduledTask -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal `
    -Description "Nightly backup task"
```

### 2.4 Register-ScheduledTask

Registers (creates or updates) a scheduled task in the scheduler service.

```powershell
Register-ScheduledTask
    [-TaskName] <String>
    [[-Action] <CimInstance[]>]
    [[-Trigger] <CimInstance[]>]
    [[-Settings] <CimInstance>]
    [[-Principal] <CimInstance>]
    [[-Description] <String>]
    [[-TaskPath] <String>]
    [[-RunLevel] <String> {Limited | Highest}]
    [-Force]
    [[-User] <String>]
    [[-Password] <String>]
    [-ConvertFromSecureString]
    [[-SettingsCompatibilityVersion] <String> {V1 | V2}]
    [[-InputObject] <CimInstance>]
    [[-CimSession] <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [<CommonParameters>]
```

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| TaskName | String | Name for the task (required). |
| TaskPath | String | Folder path; must start and end with `\`. Default: `\`. |
| Action | CimInstance[] | One or more actions. |
| Trigger | CimInstance[] | One or more triggers. |
| Settings | CimInstance | Execution settings. |
| Principal | CimInstance | Security context. |
| RunLevel | String | `Limited` (default) or `Highest` (elevated). |
| User | String | Account to run the task under. Default: current user. |
| Password | String | Password for the user account. |
| Force | Switch | Overwrite existing task without confirmation. |
| InputObject | CimInstance | A task object from New-ScheduledTask to register. |

**Examples:**

```powershell
# Simple task registration
Register-ScheduledTask -TaskName "MyBackup" `
    -Action (New-ScheduledTaskAction -Execute "robocopy.exe" "C:\Data D:\Backup /MIR") `
    -Trigger (New-ScheduledTaskTrigger -Daily -At "3AM") `
    -Description "Daily mirror backup"

# Register with explicit principal
Register-ScheduledTask -TaskName "AdminTask" `
    -Action (New-ScheduledTaskAction -Execute "cmd.exe" "/c cleanup.bat") `
    -User "SYSTEM" -RunLevel Highest -Force

# Register from pre-built task definition
$taskDef = New-ScheduledTask -Action $action -Trigger $trigger
Register-ScheduledTask -TaskName "FromDef" -InputObject $taskDef -TaskPath "\Custom\"
```

### 2.5 Set-ScheduledTask

Modifies properties of an existing registered task.

```powershell
Set-ScheduledTask
    [-TaskName] <String>
    [[-Action] <CimInstance[]>]
    [[-Trigger] <CimInstance[]>]
    [[-Settings] <CimInstance>]
    [[-Principal] <CimInstance>]
    [[-TaskPath] <String>]
    [[-User] <String>]
    [[-Password] <String>]
    [[-Description] <String>]
    [<CommonParameters>]

# With SDDL
Set-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    -Sddl <String>
    [<CommonParameters>]
```

**Example:**

```powershell
# Change trigger to weekly
Set-ScheduledTask -TaskName "MyBackup" `
    -Trigger (New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday -At "3AM")

# Update security context
Set-ScheduledTask -TaskName "MyBackup" -User "NT AUTHORITY\SYSTEM" -RunLevel Highest
```

### 2.6 Unregister-ScheduledTask

Removes (deletes) a scheduled task.

```powershell
Unregister-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [-Confirm]
    [-WhatIf]
    [<CommonParameters>]
```

**Example:**

```powershell
# Delete with confirmation prompt
Unregister-ScheduledTask -TaskName "MyBackup"

# Force delete without confirmation
Unregister-ScheduledTask -TaskName "MyBackup" -Confirm:$false
```

### 2.7 Enable-ScheduledTask

Enables a scheduled task.

```powershell
Enable-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [[-InputObject] <CimInstance>]
    [[-CimSession] <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [<CommonParameters>]
```

**Example:**

```powershell
# Enable by name
Enable-ScheduledTask -TaskName "\Microsoft\Windows\Defrag\ScheduledDefrag"

# Enable all disabled tasks in a folder
Get-ScheduledTask -TaskPath "\MyTasks\" -State Disabled |
    Enable-ScheduledTask
```

### 2.8 Disable-ScheduledTask

Disables a scheduled task (prevents it from running but preserves it).

```powershell
Disable-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [[-InputObject] <CimInstance>]
    [[-CimSession] <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [<CommonParameters>]
```

**Example:**

```powershell
# Disable a specific task
Disable-ScheduledTask -TaskName "\Microsoft\Windows\Application Experience\ProgramDataUpdater"

# Disable all CEIP tasks
Get-ScheduledTask -TaskPath "\Microsoft\Windows\Customer Experience Improvement Program\" |
    Disable-ScheduledTask
```

### 2.9 Start-ScheduledTask

Starts (runs) a scheduled task immediately, regardless of triggers.

```powershell
Start-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [[-InputObject] <CimInstance>]
    [[-CimSession] <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [<CommonParameters>]
```

**Example:**

```powershell
Start-ScheduledTask -TaskName "\MyTasks\NightlyBackup"

# Run all tasks in a folder
Get-ScheduledTask -TaskPath "\MyTasks\" | Start-ScheduledTask
```

### 2.10 Stop-ScheduledTask

Stops a running scheduled task.

```powershell
Stop-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [[-InputObject] <CimInstance>]
    [[-CimSession] <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [<CommonParameters>]
```

**Example:**

```powershell
Stop-ScheduledTask -TaskName "LongRunningTask"

# Stop all running tasks
Get-ScheduledTask -State Running | Stop-ScheduledTask
```

### 2.11 Export-ScheduledTask

Exports a scheduled task as an XML string.

```powershell
Export-ScheduledTask
    [-TaskName] <String>
    [[-TaskPath] <String>]
    [[-CimSession] <CimSession[]>]
    [-ThrottleLimit <Int32>]
    [<CommonParameters>]
```

**Example:**

```powershell
# Export single task
$xml = Export-ScheduledTask -TaskName "MyBackup"
$xml | Out-File "C:\Backup\mytask.xml"

# Export and save
Get-ScheduledTask -TaskPath "\Microsoft\Windows\Defrag\" |
    ForEach-Object {
        $xml = Export-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath
        $path = "C:\TaskExports\$($_.TaskName -replace '[\/\\]','_').xml"
        $xml | Out-File $path
    }
```

### 2.12 New-ScheduledTaskTrigger

Creates a trigger object for a scheduled task.

```powershell
# Once
New-ScheduledTaskTrigger
    -Once
    -At <DateTime>
    [-RepetitionInterval <TimeSpan>]
    [-RepetitionDuration <TimeSpan>]
    [-RandomDelay <TimeSpan>]
    [-ExpirationTime <DateTime>]
    [-Enabled <Boolean>]
    [-Id <String>]

# Daily
New-ScheduledTaskTrigger
    -Daily
    -At <DateTime>
    [-DaysInterval <Int32>]
    [-RandomDelay <TimeSpan>]

# Weekly
New-ScheduledTaskTrigger
    -Weekly
    -At <DateTime>
    -DaysOfWeek <DayOfWeek[]>
    [-WeeksInterval <Int32>]
    [-RandomDelay <TimeSpan>]

# Monthly
New-ScheduledTaskTrigger
    -Monthly
    -At <DateTime>
    -MonthsOfYear <Month[]>  # January, February, ...
    [-DaysOfMonth <Int32[]>] # 1-31
    [-LastDayOfMonth]
    [-RandomDelay <TimeSpan>]

# MonthlyDOW (Day of Week within a month)
New-ScheduledTaskTrigger
    -MonthlyDOW
    -At <DateTime>
    -DaysOfWeek <DayOfWeek[]>
    -WeeksOfMonth <WeekOfMonth[]>  # First, Second, Third, Fourth, Last
    [-MonthsOfYear <Month[]>]
    [-RandomDelay <TimeSpan>]

# AtLogon
New-ScheduledTaskTrigger -AtLogon [-User <String>] [-RandomDelay <TimeSpan>]

# AtStartup
New-ScheduledTaskTrigger -AtStartup [-RandomDelay <TimeSpan>]

# AtEvent
New-ScheduledTaskTrigger -AtEvent -EventSubscription <String> [-Delay <TimeSpan>]
# EventSubscription format: "<LogName>!/<Query>" or XPath
# Example: "System!*[System[EventID=1074]]"

# OnIdle (no additional parameters beyond the switch)
New-ScheduledTaskTrigger -OnIdle

# OnSessionStateChange
New-ScheduledTaskTrigger -OnSessionStateChange -State <StateChangeType>
# StateChangeType: ConsoleConnect, ConsoleDisconnect, RemoteConnect,
#                   RemoteDisconnect, SessionLock, SessionUnlock
```

**Examples:**

```powershell
# Run daily at 2:00 AM
New-ScheduledTaskTrigger -Daily -At "2:00AM"

# Run every 15 minutes for 8 hours
New-ScheduledTaskTrigger -Once -At (Get-Date).Date.AddHours(8) `
    -RepetitionInterval (New-TimeSpan -Minutes 15) `
    -RepetitionDuration (New-TimeSpan -Hours 8)

# Run on first Monday of every month
New-ScheduledTaskTrigger -MonthlyDOW -DaysOfWeek Monday -WeeksOfMonth First `
    -MonthsOfYear January,March,May,July,September,November `
    -At "9:00AM"

# Run when event 4624 (logon) occurs in Security log
New-ScheduledTaskTrigger -AtEvent `
    -EventSubscription "Security!*[System[EventID=4624]]"
```

### 2.13 New-ScheduledTaskAction

Creates an action object for a scheduled task.

```powershell
New-ScheduledTaskAction
    -Execute <String>
    [[-Argument] <String>]
    [[-WorkingDirectory] <String>]
    [[-WorkingDirectory] <String>]
    [-Id <String>]
    [<CommonParameters>]

New-ScheduledTaskAction
    -CimClass <CimClass>       # For COM handler actions
    [<CommonParameters>]
```

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| Execute | String | Path to executable or script. Environment variables expanded. |
| Argument | String | Command-line arguments. |
| WorkingDirectory | String | Starting directory for the process. |
| Id | String | Identifier for this action (useful when task has multiple actions). |

**Examples:**

```powershell
# Run a program
New-ScheduledTaskAction -Execute "C:\Tools\cleanup.exe" -Argument "--full" `
    -WorkingDirectory "C:\Tools"

# Run PowerShell with a script
New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File C:\Scripts\Maintenance.ps1"

# Multiple actions (run sequentially)
$action1 = New-ScheduledTaskAction -Execute "net.exe" -Argument "stop MyApp"
$action2 = New-ScheduledTaskAction -Execute "net.exe" -Argument "start MyApp"
# Pass both: -Action @($action1, $action2)

# Batch file with cmd
New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/c C:\Scripts\deploy.bat"
```

### 2.14 New-ScheduledTaskPrincipal

Creates a principal object defining the security context.

```powershell
New-ScheduledTaskPrincipal
    [-UserId] <String>
    [[-LogonType] <String> {S4U | Password | Interactive | ServiceAccount | Group | LimitedToken}]
    [-RunLevel] <String> {Limited | Highest}
    [-ProcessTokenSidType <String> {Default | Unrestricted | Restricted | AppContainer}]
    [-Id <String>]
    [-DisplayName <String>]
    [<CommonParameters>]
```

**LogonType values:**

| Value | Description |
|-------|-------------|
| S4U | Run without storing credentials (limited; no network access). |
| Password | Run with stored password (requires password). |
| Interactive | Run only when user is logged on interactively. |
| ServiceAccount | Use built-in service account (SYSTEM, LOCAL SERVICE, NETWORK SERVICE). |
| Group | Run as a group (e.g., Administrators). |
| LimitedToken | Run with restricted token. |

**Examples:**

```powershell
# Run as SYSTEM, elevated
New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\SYSTEM" `
    -LogonType ServiceAccount -RunLevel Highest

# Run as current user, interactive only
New-ScheduledTaskPrincipal -UserId "DOMAIN\jsmith" `
    -LogonType Interactive -RunLevel Limited

# Run with stored credentials (for network tasks)
New-ScheduledTaskPrincipal -UserId "DOMAIN\serviceaccount" `
    -LogonType Password -RunLevel Highest
```

### 2.15 New-ScheduledTaskSettingsSet

Creates a settings object for task execution configuration.

```powershell
New-ScheduledTaskSettingsSet
    [-AllowStartIfOnBatteries]
    [-DontStopIfGoingOnBatteries]
    [-StartWhenAvailable]
    [-RunOnlyIfNetworkAvailable]
    [-NetworkId <String>]
    [-NetworkName <String>]
    [-DisallowStartIfOnBatteries]
    [-StopIfGoingOnBatteries]
    [-WakeToRun]
    [-RunOnlyIfIdle]
    [-IdleDuration <TimeSpan>]         # Default: PT10M (10 minutes)
    [-IdleWaitTimeout <TimeSpan>]      # Default: PT1H (1 hour)
    [-ExecutionTimeLimit <TimeSpan>]   # Default: PT72H (72 hours)
    [-RestartOnFailure]
    [-RestartCount <Int32>]            # Default: 3
    [-RestartInterval <TimeSpan>]      # Default: PT1M (1 minute)
    [-MultipleInstances <String> {IgnoreNew | Parallel | Queue | StopExisting}]
    [-Priority <Int32>]                # 0 (Highest) to 10 (Lowest); default: 7
    [-Hidden]
    [-ForceStopIfGoingOnBatteries]
    [-RunningLimit <TimeSpan>]
    [-IdleOnly]
    [-Limit >]
    [-MaintenancePeriod <TimeSpan>]
    [-MaintenanceExclusive]
    [-DisallowStartOnRemoteAppSession]
    [-EnableStartupOnly]
    [[-Compatibility] <String> {V1 | V2}]
    [[-TransitTrigger] <CimInstance[]>]
    [[-CimClass] <CimClass>]
    [<CommonParameters>]
```

**Key parameters:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| AllowStartIfOnBatteries | True | Allow task to start on battery power. |
| DisallowStartIfOnBatteries | False | Prevent start on battery. |
| StopIfGoingOnBatteries | True | Stop if system switches to battery. |
| ForceStopIfGoingOnBatteries | False | Force stop (no graceful). |
| StartWhenAvailable | False | Run missed task when system becomes available. |
| RunOnlyIfNetworkAvailable | False | Only run when network is available. |
| RunOnlyIfIdle | False | Only run when system is idle. |
| WakeToRun | False | Wake system from sleep to run task. |
| ExecutionTimeLimit | 72 hours | Maximum allowed execution time. |
| RestartOnFailure | False | Restart task on failure. |
| RestartCount | 3 | Number of restart attempts. |
| RestartInterval | 1 minute | Time between restart attempts. |
| MultipleInstances | IgnoreNew | How to handle multiple instances. |
| Priority | 7 | Thread priority (0=highest, 10=lowest). |
| Hidden | False | Hide from Task Scheduler UI. |

**Example:**

```powershell
New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Hours 4) `
    -RestartOnFailure `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 5) `
    -MultipleInstances IgnoreNew `
    -Priority 4
```

---

## 3. schtasks.exe Command-Line Tool

Available on all supported Windows versions. Does not require PowerShell.

### 3.1 General Syntax

```
schtasks /<Command> /Parameters
```

Run from an elevated command prompt for administrative tasks.

### 3.2 /Query - Display Scheduled Tasks

```powershell
schtasks /Query
    [/TN <TaskName>]
    [/FO <Format> {TABLE | LIST | CSV}]
    [/V]                           # Verbose output
    [/NH]                          # No header (for CSV/TABLE)
    [/FO LIST /V]                  # Full verbose details
    [/XML]                         # Output as XML
    [/S <Server>]                  # Remote computer
    [/U <Username>]                # Alternate credentials
    [/P <Password>]
```

**Examples:**

```powershell
# List all tasks (table format)
schtasks /Query /FO TABLE

# List all tasks with verbose details
schtasks /Query /FO LIST /V

# Query a specific task
schtasks /Query /TN "\Microsoft\Windows\Defrag\ScheduledDefrag" /FO LIST /V

# Output as XML (for inspection/export)
schtasks /Query /TN "\MyTask" /XML

# CSV format for scripting
schtasks /Query /FO CSV /NH > tasks.csv

# Query remote computer
schtasks /Query /S REMOTE01 /U DOMAIN\admin /P pass123 /FO TABLE
```

### 3.3 /Create - Create a Scheduled Task

```powershell
schtasks /Create
    /TN <TaskName>
    /TR <TaskRun>                  # Command/program to execute
    /SC <ScheduleType>
        HOURLY /MO <Minutes>
        DAILY /MO <Days>
        WEEKLY /D <Days> /MO <Weeks>
        MONTHLY /D <Days> /MO <Months>
        MONTHLYDOW /D <Days> /MO <Weeks>    # Days = Mon,Tue,...
        ONCE /ST <StartTime> /SD <StartDate>
        ONSTART
        ONLOGON
        ONIDLE
        ONEVENT /EC <LogName> /EI <EventId>
    [/ST <StartTime>]              # Start time HH:MM (24h)
    [/SD <StartDate>]              # Start date MM/DD/YYYY
    [/ED <EndDate>]                # End date MM/DD/YYYY
    [/RI <Interval> /DU <Duration>]  # Repetition (FOR /SC ONCE, DAILY, WEEKLY, MONTHLY)
    [/RL <RunLevel> {LIMITED | HIGHEST}]
    [/F]                           # Force overwrite
    [/IT]                          # Interactive only (run only when user logged on)
    [/NP]                          # No password stored (run only when logged on)
    [/RLH <Priority>]
    [/M <Months>]                  # Months for MONTHLY: JAN,FEB,... or 1-12
    [/MO <Modifier>]               # Depends on schedule type
    [/D <Days>]                    # Depends on schedule type
    [/K]                           # Stop on battery
    [/Delay <Delay>]               # Delay after trigger (e.g., PT15M)
    [/XML <XMLFile>]               # Create from XML file
    [/S <Server>] [/U <User>] [/P <Password>]
```

**Examples:**

```powershell
# Daily task at 10:30 PM
schtasks /Create /TN "NightlyBackup" /TR "C:\Scripts\backup.bat" /SC DAILY /ST 22:30 /RL HIGHEST /F

# Weekly on Monday and Wednesday at 8 AM
schtasks /Create /TN "WeeklyMaintenance" /TR "powershell.exe -File C:\Maint.ps1" /SC WEEKLY /D MON,WED /ST 08:00

# Monthly on the 1st and 15th
schtasks /Create /TN "BiMonthly" /TR "C:\Reports\generate.exe" /SC MONTHLY /D 1,15 /ST 09:00

# Hourly with 15-minute intervals
schtasks /Create /TN "HourlyCheck" /TR "C:\Tools\check.exe" /SC HOURLY /MO 15 /ST 08:00

# MonthlyDOW - First Tuesday of every month
schtasks /Create /TN "PatchTuesday" /TR "C:\Scripts\patches.bat" /SC MONTHLYDOW /D TUE /MO FIRST /ST 03:00

# At logon
schtasks /Create /TN "LogonSetup" /TR "C:\Tools\logon.bat" /SC ONLOGON /IT /RL LIMITED

# One-time task
schtasks /Create /TN "OneShot" /TR "C:\Tools\once.exe" /SC ONCE /SD 12/25/2026 /ST 09:00

# From XML file
schtasks /Create /TN "FromXML" /XML "C:\TaskDefs\import.xml" /F

# With repetition: every 30 minutes for 8 hours
schtasks /Create /TN "Repeated" /TR "C:\Tools\poll.exe" /SC DAILY /ST 08:00 /RI 0000:30 /DU 0008:00
```

### 3.4 /Change - Modify a Scheduled Task

```powershell
schtasks /Change
    /TN <TaskName>
    [/TR <TaskRun>]                # New command to execute
    [/ST <StartTime>]              # New start time
    [/SD <StartDate>]              # New start date
    [/ED <EndDate>]                # New end date
    [/RL <RunLevel>]
    [/K]                           # Stop on battery (for /SC ONSTART, ONLOGON, ONIDLE)
    [/F]                           # Force change
    [/IT]                          # Interactive only
    [/NP]                          # No password stored
    [/RI <Interval> /DU <Duration>]
    [/ENABLE | /DISABLE]
    [/Delay <Delay>]
    [/S <Server>] [/U <User>] [/P <Password>]
```

**Examples:**

```powershell
# Change task to run a different program
schtasks /Change /TN "NightlyBackup" /TR "C:\Scripts\backup-v2.bat" /F

# Disable a task
schtasks /Change /TN "NightlyBackup" /DISABLE

# Enable a task
schtasks /Change /TN "NightlyBackup" /ENABLE

# Change start time
schtasks /Change /TN "NightlyBackup" /ST 23:00

# Change to highest run level
schtasks /Change /TN "NightlyBackup" /RL HIGHEST
```

### 3.5 /Run - Execute a Task Immediately

```powershell
schtasks /Run /TN <TaskName>
    [/S <Server>] [/U <User>] [/P <Password>]
```

**Example:**

```powershell
schtasks /Run /TN "\Microsoft\Windows\Defrag\ScheduledDefrag"
```

### 3.6 /End - Stop a Running Task

```powershell
schtasks /End /TN <TaskName>
    [/S <Server>] [/U <User>] [/P <Password>]
```

**Example:**

```powershell
schtasks /End /TN "NightlyBackup"
```

### 3.7 /Delete - Remove a Task

```powershell
schtasks /Delete /TN <TaskName>
    [/F]                           # Force delete without confirmation
    [/S <Server>] [/U <User>] [/P <Password>]
```

**Examples:**

```powershell
# Delete with confirmation
schtasks /Delete /TN "NightlyBackup"

# Force delete
schtasks /Delete /TN "NightlyBackup" /F
```

### 3.8 /Export - Export Task to XML

```powershell
schtasks /Export /TN <TaskName> /XML <FilePath>
    [/F]                           # Overwrite existing file
    [/S <Server>] [/U <User>] [/P <Password>]
```

**Example:**

```powershell
schtasks /Export /TN "NightlyBackup" /XML "C:\Exports\backup-task.xml" /F
```

### 3.9 /Import - Import Task from XML

```powershell
schtasks /Import /TN <TaskName> /XML <FilePath>
    [/F]                           # Overwrite if exists
    [/S <Server>] [/U <User>] [/P <Password>]
```

**Example:**

```powershell
schtasks /Import /TN "RestoredTask" /XML "C:\Exports\backup-task.xml" /F
```

---

## 4. Trigger Types

### 4.1 Overview

Triggers define when a task starts. A task can have multiple triggers; any
trigger firing will start the task (OR logic). All triggers support common
properties: StartBoundary, EndBoundary, Enabled, Repetition, and Delay.

### 4.2 Trigger Type Reference

| Trigger Type | COM Interface | XML Element | Description |
|-------------|---------------|-------------|-------------|
| **Event** | IEventTrigger | `<EventTrigger>` | Fires on a Windows Event Log entry matching XPath query. |
| **Time** | ITimeTrigger | `<TimeTrigger>` | Fires at a specific date/time, optionally repeating. |
| **Daily** | IDailyTrigger | `<CalendarTrigger><ScheduleByDay>` | Fires once per day at a given time. |
| **Weekly** | IWeeklyTrigger | `<CalendarTrigger><ScheduleByWeek>` | Fires on specific days of the week, every N weeks. |
| **Monthly** | IMonthlyTrigger | `<CalendarTrigger><ScheduleByMonth>` | Fires on specific days of specific months. |
| **MonthlyDOW** | IMonthlyDOWTrigger | `<CalendarTrigger><ScheduleByMonthDayOfWeek>` | Fires on a specific weekday occurrence (1st, 2nd, etc.) in specific months. |
| **Idle** | IIdleTrigger | `<IdleTrigger>` | Fires when the system becomes idle (if idle conditions are configured). |
| **Registration** | IRegistrationTrigger | `<RegistrationTrigger>` | Fires when the task is registered, updated, or its metadata changes. |
| **Boot** | IBootTrigger | `<BootTrigger>` | Fires at system startup, with optional delay. |
| **Logon** | ILogonTrigger | `<LogonTrigger>` | Fires when a specified user logs on (or any user). |
| **SessionStateChange** | ISessionStateChangeTrigger | `<SessionStateChangeTrigger>` | Fires on session connect, disconnect, lock, or unlock. |

### 4.3 Common Trigger Properties

```xml
<Trigger>
    <Enabled>true</Enabled>
    <StartBoundary>2026-01-01T09:00:00</StartBoundary>
    <EndBoundary>2026-12-31T23:59:59</EndBoundary>
    <Delay>PT15M</Delay>           <!-- ISO 8601 duration -->
    <Repetition>
        <Interval>PT4H</Interval>
        <Duration>PT12H</Duration>
        <StopAtDurationEnd>true</StopAtDurationEnd>
    </Repetition>
</Trigger>
```

### 4.4 Event Trigger Detail

```xml
<EventTrigger>
    <Enabled>true</Enabled>
    <Subscription>
        <![CDATA[
        <QueryList>
            <Query Id="0" Path="System">
                <Select Path="System">
                    *[System[(EventID=1074)]]
                </Select>
            </Query>
        </QueryList>
        ]]>
    </Subscription>
    <ValueQueries>
        <Value Name="Param1">Event/EventData/Data[@Name='UserName']</Value>
    </ValueQueries>
    <Delay>PT30S</Delay>
</EventTrigger>
```

PowerShell:

```powershell
# Event trigger: System log, Event ID 6005 (Event Log service started)
New-ScheduledTaskTrigger -AtEvent `
    -EventSubscription "System!*[System[EventID=6005]]" `
    -Delay (New-TimeSpan -Seconds 30)
```

### 4.5 Calendar Trigger Detail (Daily / Weekly / Monthly / MonthlyDOW)

```xml
<!-- Daily -->
<CalendarTrigger>
    <StartBoundary>2026-01-01T02:00:00</StartBoundary>
    <Enabled>true</Enabled>
    <ScheduleByDay>
        <DaysInterval>1</DaysInterval>     <!-- Every N days -->
    </ScheduleByDay>
</CalendarTrigger>

<!-- Weekly -->
<CalendarTrigger>
    <StartBoundary>2026-01-01T08:00:00</StartBoundary>
    <Enabled>true</Enabled>
    <ScheduleByWeek>
        <DaysOfWeek>
            <Monday/>
            <Wednesday/>
            <Friday/>
        </DaysOfWeek>
        <WeeksInterval>1</WeeksInterval>   <!-- Every N weeks -->
    </ScheduleByWeek>
</CalendarTrigger>

<!-- Monthly -->
<CalendarTrigger>
    <StartBoundary>2026-01-01T09:00:00</StartBoundary>
    <Enabled>true</Enabled>
    <ScheduleByMonth>
        <Months>
            <January/><March/><May/><July/>
            <September/><November/>
        </Months>
        <DaysOfMonth>
            <Day>1</Day>
            <Day>15</Day>
        </DaysOfMonth>
    </ScheduleByMonth>
</CalendarTrigger>

<!-- MonthlyDOW -->
<CalendarTrigger>
    <StartBoundary>2026-01-01T03:00:00</StartBoundary>
    <Enabled>true</Enabled>
    <ScheduleByMonthDayOfWeek>
        <Weeks>
            <Week>First</Week>          <!-- First, Second, Third, Fourth, Last -->
        </Weeks>
        <DaysOfWeek>
            <Tuesday/>
        </DaysOfWeek>
        <Months>
            <January/><February/><March/><April/>
            <May/><June/><July/><August/>
            <September/><October/><November/><December/>
        </Months>
    </ScheduleByMonthDayOfWeek>
</CalendarTrigger>
```

### 4.6 Session State Change Types

| State | XML Element | Description |
|-------|-------------|-------------|
| ConsoleConnect | `<SessionStateChange><State>ConsoleConnect</State>` | User connects to console. |
| ConsoleDisconnect | Same with `ConsoleDisconnect` | User disconnects from console. |
| RemoteConnect | Same with `RemoteConnect` | Remote desktop connection. |
| RemoteDisconnect | Same with `RemoteDisconnect` | Remote desktop disconnection. |
| SessionLock | Same with `SessionLock` | Workstation locked. |
| SessionUnlock | Same with `SessionUnlock` | Workstation unlocked. |

---

## 5. Task Conditions

Conditions are AND-ed together -- the task only starts when ALL conditions are
satisfied (in addition to triggers firing).

### 5.1 Idle Conditions

```xml
<Settings>
    <RunOnlyIfIdle>true</RunOnlyIfIdle>
    <IdleSettings>
        <Duration>PT10M</Duration>          <!-- How long idle before starting -->
        <WaitTimeout>PT1H</WaitTimeout>     <!-- Max wait for idle -->
        <StopOnIdleEnd>true</StopOnIdleEnd> <!-- Stop when user returns -->
        <RestartOnIdle>false</RestartOnIdle> <!-- Restart when idle again -->
    </IdleSettings>
</Settings>
```

| Condition | PowerShell Parameter | Default | Description |
|-----------|---------------------|---------|-------------|
| RunOnlyIfIdle | `-RunOnlyIfIdle` | False | Only run when system is idle. |
| IdleSettings.Duration | `-IdleDuration` | 10 min | How long system must be idle before task runs. |
| IdleSettings.WaitTimeout | `-IdleWaitTimeout` | 1 hour | Max time to wait for idle before skipping. |
| StopOnIdleEnd | Part of `-RunOnlyIfIdle` behavior | True | Stop task if user becomes active. |
| RestartOnIdle | Part of idle settings | False | Restart task when system becomes idle again. |

### 5.2 Network Conditions

```xml
<Settings>
    <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
    <NetworkSettings>
        <Id>{GUID}</Id>                  <!-- Specific network adapter -->
        <Name>CorpNet</Name>             <!-- Or by network name -->
    </NetworkSettings>
</Settings>
```

| Condition | PowerShell Parameter | Default | Description |
|-----------|---------------------|---------|-------------|
| RunOnlyIfNetworkAvailable | `-RunOnlyIfNetworkAvailable` | False | Only run when any network is available. |
| NetworkId | `-NetworkId` | None | GUID of specific network adapter. |
| NetworkName | `-NetworkName` | None | Name of the network connection. |

### 5.3 Power Conditions

```xml
<Settings>
    <DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>
    <ForceStopIfGoingOnBatteries>false</ForceStopIfGoingOnBatteries>
    <WakeToRun>false</WakeToRun>
    <AllowStartIfOnBatteries>true</AllowStartIfOnBatteries>
</Settings>
```

| Condition | PowerShell Parameter | Default | Description |
|-----------|---------------------|---------|-------------|
| DisallowStartIfOnBatteries | `-DisallowStartIfOnBatteries` | False | Do not start task on battery power. |
| StopIfGoingOnBatteries | `-StopIfGoingOnBatteries` | True | Stop running task when switching to battery. |
| ForceStopIfGoingOnBatteries | `-ForceStopIfGoingOnBatteries` | False | Hard kill (no graceful shutdown) on battery switch. |
| AllowStartIfOnBatteries | `-AllowStartIfOnBatteries` | True | Start even on battery (opposite of DisallowStart...). |
| WakeToRun | `-WakeToRun` | False | Wake system from sleep/hibernate to run task. |

**Note:** `DisallowStartIfOnBatteries` and `AllowStartIfOnBatteries` are
mutually exclusive; setting one clears the other.

### 5.4 Time Limit and Start When Available

```xml
<Settings>
    <ExecutionTimeLimit>PT72H</ExecutionTimeLimit>   <!-- Max runtime -->
    <StartWhenAvailable>true</StartWhenAvailable>    <!-- Run missed instance -->
</Settings>
```

| Condition | PowerShell Parameter | Default | Description |
|-----------|---------------------|---------|-------------|
| ExecutionTimeLimit | `-ExecutionTimeLimit` | 72 hours | Maximum time task is allowed to run. Task is killed when exceeded. |
| StartWhenAvailable | `-StartWhenAvailable` | False | Run the task at the next available time if a scheduled run was missed. |

**ISO 8601 Duration format:** `P[n]Y[n]M[n]DT[n]H[n]M[n]S`
Examples: `PT30S` (30 seconds), `PT15M` (15 minutes), `PT2H30M` (2 hours 30 min),
`P1D` (1 day), `PT72H` (72 hours).

### 5.5 Additional Execution Conditions

```xml
<Settings>
    <Priority>7</Priority>                    <!-- 0 (highest) to 10 (lowest) -->
    <RestartOnFailure>
        <Interval>PT1M</Interval>             <!-- Between restart attempts -->
        <Count>3</Count>                      <!-- Max restart attempts -->
    </RestartOnFailure>
    <MultipleInstances>IgnoreNew</MultipleInstances>
    <!-- IgnoreNew | Parallel | Queue | StopExisting -->
    <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
    <RunOnlyWhenAvailable>false</RunOnlyWhenAvailable>
    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
</Settings>
```

### 5.6 Conditions Decision Matrix

| Scenario | Conditions to Set |
|----------|-------------------|
| Light background cleanup | RunOnlyIfIdle + StopOnIdleEnd + ExecutionTimeLimit=PT2H + DisallowStartIfOnBatteries |
| Network-dependent sync | RunOnlyIfNetworkAvailable + WakeToRun=false + StartWhenAvailable |
| System maintenance | WakeToRun + RunLevel=Highest + StartWhenAvailable + ExecutionTimeLimit=PT4H |
| User-facing notification | RunOnlyIfIdle=false + StopIfGoingOnBatteries=false + RunLevel=Limited |
| Server batch job | StartWhenAvailable + ExecutionTimeLimit + RestartOnFailure + MultipleInstances=Queue |

---

## 6. Common System Tasks - Safety Assessment

### 6.1 Assessment Table

Disabling tasks saves minor CPU/disk I/O and reduces background activity.
Always test disabling one task at a time and monitor for issues.

| Task Path | What It Does | Safe to Disable? | Priority to Disable | Notes |
|-----------|-------------|-----------------|-------------------|-------|
| `\Microsoft\Windows\Defrag\ScheduledDefrag` | Runs automatic disk defragmentation on mechanical drives. SSDs do not benefit. | **YES** (SSDs) / **NO** (HDDs) | Medium (SSDs) / Low (HDDs) | SSDs have no fragmentation issue; defragging an SSD wastes write cycles. HDDs benefit from periodic defrag. |
| `\Microsoft\Windows\DiskCleanup\SilentCleanup` | Runs disk cleanup automatically when disk space is low. | **NO** | N/A | Prevents disk space exhaustion. Removing it risks out-of-space errors. |
| `\Microsoft\Windows\Customer Experience Improvement Program\Consolidator` | Collects and sends CEIP telemetry data to Microsoft. | **YES** | High | Telemetry only; no functional impact. Privacy improvement. |
| `\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip` | Collects USB device telemetry for CEIP. | **YES** | High | Same as above; USB-specific telemetry. |
| `\Microsoft\Windows\Feedback\Siuf\DmClient` | Collects and transmits user feedback (Surface Input Usability Feedback). | **YES** | High | Feedback telemetry; no functional impact. |
| `\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload` | Downloads feedback scenario configurations. | **YES** | High | Companion to the above. |
| `\Microsoft\Windows\Maintenance\WinSAT` | Runs Windows System Assessment Tool to measure performance metrics. | **YES** | Medium | Used by Windows Experience Index. Most users never reference these scores. |
| `\Microsoft\Windows\Windows Error Reporting\QueueReporting` | Queues error reports for submission to Microsoft. | **YES** | High | WER telemetry. Disabling prevents crash/error data upload. |
| `\Microsoft\Windows\WindowsUpdate\Scheduled Start` | Checks for Windows Updates. | **NO** | N/A | Critical for security patching. Never disable. |
| `\Microsoft\Windows\UpdateOrchestrator\Reboot` | Reboots the system after Windows Updates require it. | **NO** | N/A | Required for update completion. |
| `\Microsoft\Windows\Maintenance\WinSAT` | Automatic disk defragmentation metadata collection. | **YES** | Low | Minor; see Defrag entry. |
| `\Microsoft\Windows\SystemRestore\SR` | Creates automatic system restore points. | **CAUTION** | Low | Provides recovery safety net. Disabling removes rollback capability. Recommended: keep enabled. |
| `\Microsoft\Windows\Time Synchronization\SynchronizeTime` | Synchronizes system clock with NTP server. | **NO** | N/A | Clock drift causes certificate, auth, and logging issues. |
| `\Microsoft\Windows\Diagnosis\Scheduled` | Runs scheduled diagnostic checks. | **YES** | Medium | Proactive diagnostics; rarely catches actionable issues. |
| `\Microsoft\Windows\Diagnosis\Recommended` | Runs recommended diagnostics. | **YES** | Medium | Similar to above. |
| `\Microsoft\Windows\Maps\MapsToastTask` | Checks for map updates. | **YES** | High | Only relevant if you use Windows Maps. |
| `\Microsoft\Windows\Maps\MapsUpdateTask` | Updates offline map data. | **YES** | High | Same as above. |
| `\Microsoft\Windows\RemoteAssistance\RemoteAssistanceTask` | Periodically checks for remote assistance invitations. | **YES** | High | Only needed in enterprise helpdesk scenarios. |
| `\Microsoft\Windows\FamilySafety\Monitor` | Parental controls and family safety monitoring. | **CAUTION** | N/A | Required if using Family Safety. Disable only if no family accounts. |
| `\Microsoft\Windows\PushToInstall\Registration` | Registers device for Push-to-Install (Intune/MDM provisioning). | **YES** | High | Only needed in enterprise MDM environments. |
| `\Microsoft\Windows\WiFi\WiFiTask` | Manages Wi-Fi Sense and Wi-Fi connectivity tasks. | **YES** | Medium | Wi-Fi Sense was deprecated; task may be vestigial. |
| `\Microsoft\Windows\Application Experience\ProgramDataUpdater` | Collects application compatibility data for CEIP. | **YES** | High | Telemetry data for app compatibility. |
| `\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser` | Scans installed apps for compatibility issues. | **YES** | Medium | Telemetry; Windows Update uses its own mechanism. |
| `\Microsoft\Windows\Application Experience\ProgramDataStartup` | Tracks startup programs for performance analysis. | **YES** | Medium | Minor telemetry. |
| `\Microsoft\Windows\AppxDeploymentClient\Pre-staged app cleanup` | Cleans up pre-staged app packages. | **YES** | Low | Removes leftover provisioning packages. Minimal impact. |
| `\Microsoft\Windows\CloudExperienceHost\CreateObjectTask` | Used by Windows Hello / cloud experience provisioning. | **CAUTION** | Low | May be needed for Windows Hello or cloud sign-in. Keep if using those features. |
| `\Microsoft\Windows\TextServicesFramework\MsCtfMonitor` | Monitors Text Services Framework (input methods, IME). | **NO** (if using CJK input) / **YES** (otherwise) | Medium (CJK) / High (non-CJK) | Required for Chinese/Japanese/Korean input methods. Safe for English-only systems. |
| `\Microsoft\Windows\Compatibility\ResolutionCompatibility` | Application resolution compatibility checks. | **YES** | Low | Legacy compatibility shim checks. Minimal user impact. |
| `\Microsoft\Windows\DiskFootprint\Diagnostics` | Collects disk health and footprint telemetry. | **YES** | Medium | Telemetry about disk usage patterns. |

### 6.2 Disabling Tasks

**PowerShell (recommended):**

```powershell
# Disable a single task
Disable-ScheduledTask -TaskPath "\Microsoft\Windows\Customer Experience Improvement Program\" `
    -TaskName "Consolidator"

# Disable all CEIP tasks
Get-ScheduledTask -TaskPath "\Microsoft\Windows\Customer Experience Improvement Program\" |
    Disable-ScheduledTask

# Bulk disable (use with caution)
$tasksToDisable = @(
    "\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"
    "\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"
    "\Microsoft\Windows\Feedback\Siuf\DmClient"
    "\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"
    "\Microsoft\Windows\Windows Error Reporting\QueueReporting"
    "\Microsoft\Windows\Application Experience\ProgramDataUpdater"
    "\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"
)
foreach ($t in $tasksToDisable) {
    $name = Split-Path $t -Leaf
    $path = Split-Path $t
    Disable-ScheduledTask -TaskName $name -TaskPath "$path\" -ErrorAction SilentlyContinue
    Write-Host "Disabled: $t"
}
```

**schtasks.exe:**

```powershell
schtasks /Change /TN "\Microsoft\Windows\Customer Experience Improvement Program\Consolidator" /DISABLE
```

### 6.3 Re-enabling Tasks

```powershell
Enable-ScheduledTask -TaskPath "\Microsoft\Windows\Customer Experience Improvement Program\" `
    -TaskName "Consolidator"
```

---

## 7. Task XML Format

### 7.1 XML Schema

Task XML follows the schema defined in `taskschd.xsd` (located in the Windows SDK
or at `%SystemRoot%\System32\Tasks`). The namespace is:
`http://schemas.microsoft.com/windows/2004/02/mit/task`.

Full schema download:
`https://docs.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-schema`

### 7.2 Complete Task XML Template

```xml
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4"
    xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">

    <!-- Registration metadata -->
    <RegistrationInfo>
        <Date>2026-01-15T10:00:00</Date>
        <Author>DOMAIN\jsmith</Author>
        <Version>1.0.0</Version>
        <Description>Performs nightly system backup to NAS share.</Description>
        <URI>\Custom\NightlyBackup</URI>
        <Documentation>https://internal.wiki/backup-procedure</Documentation>
        <Source>
            <Manual />
        </Source>
    </RegistrationInfo>

    <!-- Trigger definitions -->
    <Triggers>
        <CalendarTrigger>
            <Repetition>
                <Interval>PT4H</Interval>
                <Duration>PT12H</Duration>
                <StopAtDurationEnd>true</StopAtDurationEnd>
            </Repetition>
            <StartBoundary>2026-01-15T22:00:00</StartBoundary>
            <Enabled>true</Enabled>
            <EndBoundary>2026-12-31T23:59:59</EndBoundary>
            <ScheduleByDay>
                <DaysInterval>1</DaysInterval>
            </ScheduleByDay>
        </CalendarTrigger>
    </Triggers>

    <!-- Action definitions (executed sequentially) -->
    <Actions Context="Author">
        <Exec>
            <Command>powershell.exe</Command>
            <Arguments>-NoProfile -ExecutionPolicy Bypass -File "C:\Scripts\backup.ps1"</Arguments>
            <WorkingDirectory>C:\Scripts</WorkingDirectory>
        </Exec>
    </Actions>

    <!-- Security principal -->
    <Principals>
        <Principal id="Author">
            <UserId>NT AUTHORITY\SYSTEM</UserId>
            <LogonType>ServiceAccount</LogonType>
            <RunLevel>HighestAvailable</RunLevel>
        </Principal>
    </Principals>

    <!-- Execution settings -->
    <Settings>
        <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
        <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
        <StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>
        <AllowHardTerminate>true</AllowHardTerminate>
        <StartWhenAvailable>true</StartWhenAvailable>
        <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
        <IdleSettings>
            <StopOnIdleEnd>true</StopOnIdleEnd>
            <RestartOnIdle>false</RestartOnIdle>
        </IdleSettings>
        <AllowStartOnDemand>true</AllowStartOnDemand>
        <Enabled>true</Enabled>
        <Hidden>false</Hidden>
        <RunOnlyIfIdle>false</RunOnlyIfIdle>
        <WakeToRun>false</WakeToRun>
        <ExecutionTimeLimit>PT4H</ExecutionTimeLimit>
        <Priority>7</Priority>
        <RestartOnFailure>
            <Interval>PT1M</Interval>
            <Count>3</Count>
        </RestartOnFailure>
        <NetworkSettings>
            <Id>{NETWORK-GUID-HERE}</Id>
        </NetworkSettings>
    </Settings>

    <!-- Informational properties -->
    <Properties>
        <Version>4</Version>
        <Authors>DOMAIN\jsmith</Authors>
        <SourcePath>C:\TaskDefs\NightlyBackup.xml</SourcePath>
        <LastRunTime>2026-01-20T02:15:00</LastRunTime>
        <NextRunTime>2026-01-21T02:15:00</NextRunTime>
        <LastResult>0</LastResult>
        <NumberOfMissedRuns>0</NumberOfMissedRuns>
    </Properties>

    <!-- Data (custom application data) -->
    <Data>
        <BackupType>Incremental</BackupType>
        <Destination>\\nas\backups</Destination>
    </Data>

</Task>
```

### 7.3 Key XML Elements

| Element | Parent | Description |
|---------|--------|-------------|
| `<Task>` | Root | Version attribute (1.0 through 1.4). |
| `<RegistrationInfo>` | `<Task>` | Metadata: author, description, date, URI, version. |
| `<Triggers>` | `<Task>` | Container for all trigger elements. |
| `<TimeTrigger>` | `<Triggers>` | One-time or repeating calendar trigger. |
| `<CalendarTrigger>` | `<Triggers>` | Recurring schedule (daily, weekly, monthly). |
| `<EventTrigger>` | `<Triggers>` | Trigger on Windows Event Log entry. |
| `<BootTrigger>` | `<Triggers>` | Trigger at system startup. |
| `<LogonTrigger>` | `<Triggers>` | Trigger at user logon. |
| `<IdleTrigger>` | `<Triggers>` | Trigger when system is idle. |
| `<RegistrationTrigger>` | `<Triggers>` | Trigger when task is registered/modified. |
| `<SessionStateChangeTrigger>` | `<Triggers>` | Trigger on session state changes. |
| `<Actions>` | `<Task>` | Container for action elements. Context attribute links to Principal. |
| `<Exec>` | `<Actions>` | Execute a program or script. |
| `<ComHandler>` | `<Actions>` | Invoke a COM server. |
| `<SendEmail>` | `<Actions>` | Send an email (deprecated in Windows 8+). |
| `<ShowMessage>` | `<Actions>` | Display a message box (deprecated in Windows 8+). |
| `<Principals>` | `<Task>` | Container for Principal definitions. |
| `<Principal>` | `<Principals>` | Security context: UserId, LogonType, RunLevel. |
| `<Settings>` | `<Task>` | Execution settings and conditions. |
| `<IdleSettings>` | `<Settings>` | Idle duration, wait timeout, stop/restart behavior. |
| `<RestartOnFailure>` | `<Settings>` | Restart interval and count on failure. |
| `<NetworkSettings>` | `<Settings>` | Network adapter filter for RunOnlyIfNetworkAvailable. |
| `<Properties>` | `<Task>` | Read-only runtime properties (last run, next run, result). |
| `<Data>` | `<Task>` | Application-specific custom data. |

### 7.4 Security Descriptors (SDDL)

Tasks use Windows security descriptors to control who can read, modify, or
execute them. The security descriptor is stored as an SDDL string.

**Common SDDL patterns:**

```
# Full control for Administrators, Read for Users, System has full control
D:P(A;;FA;;;BA)(A;;FRFX;;;BU)(A;;FA;;;SY)

# Read-only for standard users, full for SYSTEM and Administrators
D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)

# Only SYSTEM can access
D:P(A;;FA;;;SY)
```

**SDDL Components:**

| Component | Meaning |
|-----------|---------|
| D: | DACL (Discretionary ACL) |
| P | Protected (inheritance disabled) |
| A;;FA;;;SY | Allow (A), full access (FA), for SYSTEM account (SY) |
| A;;FA;;;BA | Allow full access for built-in Administrators (BA) |
| A;;FRFX;;;BU | Allow file read + file read execute for basic users (BU) |
| A;;0x1200a9;;;BU | Allow read+execute+read attributes+read extended attributes for users |

**Retrieving task SDDL:**

```powershell
# Via schtasks
schtasks /Query /TN "\MyTask" /XML

# Via PowerShell (requires .NET TaskScheduler)
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$folder = $scheduler.GetFolder("\")
$task = $folder.GetTask("MyTask")
$task.GetSecurityDescriptor(0xF)  # DACL_SECURITY_INFORMATION
```

### 7.5 Modifying Task XML

```powershell
# Export task XML
$xml = Export-ScheduledTask -TaskName "MyTask" | Out-String
[xml]$doc = $xml

# Modify a setting
$settings = $doc.Task.Settings
$settings.DisallowStartIfOnBatteries = "false"
$settings.ExecutionTimeLimit = "PT2H"
$settings.StartWhenAvailable = "true"

# Add a trigger
$newTrigger = $doc.CreateElement("BootTrigger", "http://schemas.microsoft.com/windows/2004/02/mit/task")
$enabled = $doc.CreateElement("Enabled", "http://schemas.microsoft.com/windows/2004/02/mit/task")
$enabled.InnerText = "true"
$newTrigger.AppendChild($enabled) | Out-Null
$delay = $doc.CreateElement("Delay", "http://schemas.microsoft.com/windows/2004/02/mit/task")
$delay.InnerText = "PT5M"
$newTrigger.AppendChild($delay) | Out-Null
$doc.Task.Triggers.AppendChild($newTrigger) | Out-Null

# Re-register the modified task
Register-ScheduledTask -TaskName "MyTask" -Xml $doc.OuterXml -Force
```

### 7.6 Task Versions

| Version | OS Support | Features Added |
|---------|-----------|----------------|
| 1.0 | Windows Vista / Server 2008 | Base schema. |
| 1.1 | Windows 7 / Server 2008 R2 | ComHandler action, maintenance settings. |
| 1.2 | Windows 8 / Server 2012 | Multiple actions, network settings, process token SID type, demand start, transited triggers. |
| 1.3 | Windows 8.1 / Server 2012 R2 | DisallowStartOnRemoteAppSession, UseUnifiedSchedulingEngine, MaintenanceExclusive. |
| 1.4 | Windows 10 / Server 2016+ | Block idle start, ForceStartWhenAvailable, AllowStartOnDemand in background. |

### 7.7 Task Storage Locations

| Location | Description |
|----------|-------------|
| `%SystemRoot%\System32\Tasks\` | Task XML files, mirroring the folder structure. |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\` | Registry index for task discovery and state. |
| `%SystemRoot%\System32\Tasks\Microsoft\Windows\` | Built-in Windows system tasks. |

---

## Quick Reference: Common Operations

```powershell
# List all enabled tasks
Get-ScheduledTask | Where-Object {$_.State -ne 'Disabled'} | Select-Object TaskName, TaskPath, State

# Find tasks that run on battery (power-wasters)
Get-ScheduledTask | Where-Object {
    $_.Settings.DisallowStartIfOnBatteries -eq $false -and
    $_.Settings.StopIfGoingOnBatteries -eq $false
} | Select-Object TaskName, TaskPath

# Find tasks with no execution time limit
Get-ScheduledTask | Where-Object {
    $_.Settings.ExecutionTimeLimit -eq "PT0S" -or
    $_.Settings.ExecutionTimeLimit -eq "P0D" -or
    $_.Settings.ExecutionTimeLimit -eq "0"
} | Select-Object TaskName, TaskPath

# Show tasks by last result
Get-ScheduledTask | Get-ScheduledTaskInfo |
    Where-Object {$_.LastRunTime -ne [datetime]::MinValue} |
    Sort-Object LastTaskResult |
    Select-Object @{N='Task';E={$_.TaskName}}, LastRunTime, LastTaskResult

# Export all tasks to individual XML files
Get-ScheduledTask | ForEach-Object {
    $path = "C:\TaskExport$($_.TaskPath -replace '[\/\\]','_')$($_.TaskName).xml"
    Export-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath |
        Out-File $path -Encoding UTF8
}

# Count tasks by state
Get-ScheduledTask | Group-Object State | Select-Object Name, Count

# Find tasks running as SYSTEM
Get-ScheduledTask | Where-Object {$_.Principal.UserId -eq "NT AUTHORITY\SYSTEM"} |
    Select-Object TaskName, TaskPath
```

---

*Reference version: 2026-08-31*
*Applies to: Windows 10 20H2+ / Windows 11 / Windows Server 2019+*
*PowerShell version: 5.1+ (Windows PowerShell) and 7.x+ (PowerShell Core)*
