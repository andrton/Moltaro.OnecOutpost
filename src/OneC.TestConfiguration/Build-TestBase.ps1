# Moltaro.OnecOutpost: builds the file base of the 1C test configuration on a lab machine.
#
# Run it from the repository root in Windows PowerShell 5.1:
#
#   powershell -ExecutionPolicy Bypass -File src\OneC.TestConfiguration\Build-TestBase.ps1
#
# It reads lab.local.json next to this script, and creates that file with generated passwords when it is missing.
# Then it deletes the test base, creates it again as a file base, loads the configuration of the Configuration
# folder into it with the Designer, and creates the users through the COM connector. The passwords never reach a
# command line, a log or the screen. Exit code 0: the base is ready; 1: the script stopped, and says why.

$ErrorActionPreference = "Stop"

# The platform that builds the base: the oldest one the library supports.
$platformVersion = "8.3.17.1851"

$labPath = Join-Path $PSScriptRoot "lab.local.json"
$sourcesPath = Join-Path $PSScriptRoot "Configuration"

# The file the script writes into every base it builds. A rerun deletes the base folder only when it holds this
# file or nothing.
$markerName = "outpost-test-base.txt"

# The /Out log of the Designer. Each run of 1cv8.exe overwrites it; the script deletes it when it ends.
$logPath = Join-Path $env:TEMP "outpost-test-base.log"

function Write-Step([string] $Text) {
    Write-Host ""
    Write-Host ("== " + $Text)
}

# 24 characters of a 64-character alphabet from the cryptographic generator of Windows: 144 bits, without bias
# since 64 divides 256, and with no character that JSON or a 1C connection string would have to escape.
function New-Password {
    $alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
    $bytes = New-Object byte[] 24
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    $characters = foreach ($byte in $bytes) {
        $alphabet[$byte % 64]
    }
    return -join $characters
}

# Writes lab.local.json with the default base path, which Git ignores, and new passwords.
function New-LabFile([string] $Path) {
    $repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $lab = [ordered] @{
        testBase = [ordered] @{
            path = (Join-Path $repositoryRoot ".local\test-base")
            administrator = [ordered] @{ user = "outpost-admin"; password = (New-Password) }
            read = [ordered] @{ user = "outpost-read"; password = (New-Password) }
            write = [ordered] @{ user = "outpost-write"; password = (New-Password) }
        }
    }
    $json = ConvertTo-Json -InputObject $lab -Depth 5

    # Without a BOM: Set-Content -Encoding UTF8 of Windows PowerShell 5.1 would write one.
    [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding $false))
}

function Read-LabFile([string] $Path) {
    # ConvertFrom-Json of Windows PowerShell 5.1 quotes the text it cannot parse in its error, and that text may be
    # a password. The error is replaced by one that names only the file.
    try {
        $lab = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) | ConvertFrom-Json
    }
    catch {
        throw ($Path + " is not valid JSON.")
    }

    $testBase = $lab.testBase
    if ($null -eq $testBase -or -not $testBase.path) {
        throw ($Path + " gives no testBase.path.")
    }

    # The base folder is deleted, so its path must depend on no current folder or drive: it has to be a full path
    # already, as GetFullPath would write it.
    if ([IO.Path]::GetFullPath($testBase.path) -ne $testBase.path) {
        throw ($Path + " gives testBase.path " + $testBase.path + ", which is not a full path in its plain form, such as C:\lab\test-base.")
    }

    foreach ($account in "administrator", "read", "write") {
        $entry = $testBase.$account
        if ($null -eq $entry -or -not $entry.user -or -not $entry.password) {
            throw ($Path + " gives no user and password in testBase." + $account + ".")
        }
    }
    return $testBase
}

# Finds 1cv8.exe of the version in the Program Files of either bitness. ProgramW6432 names the 64-bit one even
# in a 32-bit process, where ProgramFiles names the 32-bit one.
function Find-Designer([string] $Version) {
    $roots = @($env:ProgramW6432, $env:ProgramFiles, ${env:ProgramFiles(x86)}) |
        Where-Object { $_ } |
        Select-Object -Unique |
        ForEach-Object { Join-Path $_ "1cv8" }

    foreach ($root in $roots) {
        $designer = Join-Path $root ($Version + "\bin\1cv8.exe")
        if (Test-Path -LiteralPath $designer) {
            return $designer
        }
    }

    $installed = foreach ($root in $roots) {
        Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "bin\1cv8.exe") } |
            ForEach-Object { $_.FullName }
    }
    $message = "1C:Enterprise " + $Version + " is not installed: no bin\1cv8.exe under " + ($roots -join " or ") + "."
    if ($installed) {
        $message += " Installed: " + ($installed -join ", ") + "."
    }
    else {
        $message += " No version of 1C:Enterprise is installed there."
    }
    throw $message
}

# The file that processes of one bitness load for V83.COMConnector, or $null when it is not registered for them.
# Windows keeps the class registrations of the two bitnesses apart.
function Get-ConnectorPath([Microsoft.Win32.RegistryView] $View) {
    $classes = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::ClassesRoot, $View)
    try {
        $classIdKey = $classes.OpenSubKey("V83.COMConnector\CLSID")
        if ($null -eq $classIdKey) {
            return $null
        }
        $serverKey = $classes.OpenSubKey("CLSID\" + $classIdKey.GetValue("") + "\InprocServer32")
        if ($null -eq $serverKey) {
            return $null
        }
        return $serverKey.GetValue("")
    }
    finally {
        $classes.Close()
    }
}

# Returns the connector this PowerShell would load. COM loads only a connector of the bitness of the process, so
# one registered for the other bitness fails with "class not registered"; this says what to do instead.
function Assert-Connector([string] $Version) {
    if ([Environment]::Is64BitProcess) {
        $bitness = "64-bit"
        $otherBitness = "32-bit"
        $view = [Microsoft.Win32.RegistryView]::Registry64
        $otherView = [Microsoft.Win32.RegistryView]::Registry32
        $otherPowerShell = "C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
    }
    else {
        $bitness = "32-bit"
        $otherBitness = "64-bit"
        $view = [Microsoft.Win32.RegistryView]::Registry32
        $otherView = [Microsoft.Win32.RegistryView]::Registry64
        $otherPowerShell = "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
    }

    $path = Get-ConnectorPath $view
    if ($path) {
        return $path
    }

    $message = "The COM connector V83.COMConnector is not registered for " + $bitness + " processes, and this Windows PowerShell is " + $bitness + "."
    $otherPath = Get-ConnectorPath $otherView
    if ($otherPath) {
        $message += " It is registered for " + $otherBitness + " processes, as " + $otherPath + ": either run this script in the " + $otherBitness + " Windows PowerShell, " + $otherPowerShell + ", or register the " + $bitness + " connector."
    }

    # ProgramFiles names the Program Files of the bitness of this process, where the matching connector is.
    $matchingPath = Join-Path $env:ProgramFiles ("1cv8\" + $Version + "\bin\comcntr.dll")
    if (Test-Path -LiteralPath $matchingPath) {
        $message += " To register the " + $bitness + " connector, run as administrator: regsvr32 `"" + $matchingPath + "`""
    }
    else {
        $message += " The " + $bitness + " 1C:Enterprise " + $Version + " is not installed, so " + $matchingPath + " is missing."
    }
    throw $message
}

# Deletes the base folder, but only when it is empty or holds the marker of a base this script built. A wrong path
# in lab.local.json, even one that points at another 1C file base, then stops the script instead of deleting the
# folder. A folder that holds a copy of the marker is not told apart.
function Remove-TestBase([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $isEmpty = @(Get-ChildItem -LiteralPath $Path -Force).Count -eq 0
    $holdsMarker = Test-Path -LiteralPath (Join-Path $Path $script:markerName) -PathType Leaf
    if (-not $isEmpty -and -not $holdsMarker) {
        throw ($Path + " is not empty and holds no " + $script:markerName + ", so this script did not build it and does not delete it. Move it away, or give another testBase.path in lab.local.json.")
    }

    Write-Host ("Deleting the previous base in " + $Path)
    Remove-Item -LiteralPath $Path -Recurse -Force
}

function Write-Marker([string] $Path) {
    $text = "Build-TestBase.ps1 of Moltaro.OnecOutpost built the 1C test base in this folder, and deletes the folder when it runs again."
    [IO.File]::WriteAllText((Join-Path $Path $script:markerName), $text, (New-Object Text.UTF8Encoding $false))
}

function Write-PlatformLog {
    if (-not (Test-Path -LiteralPath $script:logPath)) {
        return
    }

    # 1C:Enterprise 8.3.17 writes the log in the Cyrillic code page of Windows, without a BOM. A log that starts
    # with the BOM of UTF-8 is read as UTF-8.
    $bytes = [IO.File]::ReadAllBytes($script:logPath)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $text = [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3).Trim()
    }
    else {
        $text = [Text.Encoding]::GetEncoding(1251).GetString($bytes).Trim()
    }
    if ($text) {
        Write-Host "--- log of 1cv8.exe ---"
        Write-Host $text
        Write-Host "--- end of the log ---"
    }
}

# Runs 1cv8.exe and waits for it. It is a GUI program, which a console does not wait for; WaitForExit waits for
# this process alone and gives its exit code. The log is printed after every run, since the Designer can report a
# problem in it, such as a property it dropped, and still end with 0.
function Invoke-Platform([string] $Designer, [string] $Arguments) {
    if (Test-Path -LiteralPath $script:logPath) {
        Remove-Item -LiteralPath $script:logPath
    }

    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Designer

    # 1C reads a path inside quotes. No password is ever part of these arguments.
    $start.Arguments = $Arguments + " /Out `"" + $script:logPath + "`""
    $start.UseShellExecute = $false

    $process = [Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    $platformExitCode = $process.ExitCode
    $process.Dispose()

    Write-PlatformLog
    if ($platformExitCode -ne 0) {
        throw ("1cv8.exe ended with exit code " + $platformExitCode + "; its log, if it wrote one, is above.")
    }
}

# The connection and the objects of 1C are IDispatch without type information, which PowerShell cannot read with
# a dot. They are reached by name. The leading comma keeps PowerShell from unrolling a 1C collection, such as
# Metadata.Roles, into its items when the function returns it.
function Get-ComProperty($Object, [string] $Name) {
    return , ([System.__ComObject].InvokeMember($Name, [Reflection.BindingFlags]::GetProperty, $null, $Object, $null))
}

# PowerShell may wrap a value in a PSObject; COM needs the bare value, or 1C sees a wrong type.
function Get-BareArguments([object[]] $Arguments) {
    $bare = New-Object object[] $Arguments.Length
    for ($index = 0; $index -lt $Arguments.Length; $index++) {
        if ($null -ne $Arguments[$index]) {
            $bare[$index] = $Arguments[$index].PSObject.BaseObject
        }
    }
    return , $bare
}

function Set-ComProperty($Object, [string] $Name, $Value) {
    $arguments = Get-BareArguments @($Value)
    [void] ([System.__ComObject].InvokeMember($Name, [Reflection.BindingFlags]::SetProperty, $null, $Object, $arguments))
}

function Invoke-ComMethod($Object, [string] $Name, [object[]] $Arguments) {
    $bare = Get-BareArguments $Arguments
    return , ([System.__ComObject].InvokeMember($Name, [Reflection.BindingFlags]::InvokeMethod, $null, $Object, $bare))
}

function New-TestUser($Connection, $Account, [string] $RoleName) {
    $roles = Get-ComProperty (Get-ComProperty $Connection "Metadata") "Roles"
    $role = Invoke-ComMethod $roles "Find" @($RoleName)
    if ($null -eq $role) {
        throw ("The base has no role " + $RoleName + ": the Designer did not load it. Its log is above.")
    }

    $user = Invoke-ComMethod (Get-ComProperty $Connection "InfoBaseUsers") "CreateUser" @()
    Set-ComProperty $user "Name" $Account.user
    Set-ComProperty $user "StandardAuthentication" $true
    Set-ComProperty $user "Password" $Account.password
    [void] (Invoke-ComMethod (Get-ComProperty $user "Roles") "Add" @($role))
    [void] (Invoke-ComMethod $user "Write" @())

    Write-Host ($Account.user + ", role " + $RoleName)
}

# The Designer's command line cannot create users, so the COM connector does. A base with no users accepts a
# connection without a user and gives it every right. 1C refuses to write the first user unless it has the
# Administration right, so the administrator comes first.
function New-TestUsers([string] $BasePath, $TestBase) {
    $connector = $null
    $connection = $null
    try {
        $connector = New-Object -ComObject V83.COMConnector
        $connection = $connector.Connect("File=`"" + $BasePath + "`";")
        New-TestUser $connection $TestBase.administrator "Administrator"
        New-TestUser $connection $TestBase.read "OutpostRead"
        New-TestUser $connection $TestBase.write "OutpostWrite"
    }
    finally {
        # The session ends, and the base is free, once nothing refers to the connection any more.
        $connection = $null
        $connector = $null
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
        [GC]::Collect()
    }
}

$scriptExitCode = 0
$testBase = $null
try {
    Write-Step ("Reading " + $labPath)
    if (-not (Test-Path -LiteralPath $labPath)) {
        New-LabFile $labPath
        Write-Host "It was missing, so the script created it with the default base path and generated passwords."
    }
    $testBase = Read-LabFile $labPath
    # Without a trailing backslash, which would sit right before the closing quote on the command line of 1C.
    $basePath = $testBase.path.TrimEnd([char] "\")

    Write-Step ("Finding 1C:Enterprise " + $platformVersion)
    $designer = Find-Designer $platformVersion
    Write-Host $designer

    Write-Step "Checking the COM connector"
    Write-Host (Assert-Connector $platformVersion)

    Write-Step ("Creating the file base in " + $basePath)
    Remove-TestBase $basePath
    [void] [IO.Directory]::CreateDirectory($basePath)
    Invoke-Platform $designer ("CREATEINFOBASE File=`"" + $basePath + "`" /DisableStartupDialogs")
    Write-Marker $basePath

    Write-Step "Loading the configuration and updating the database configuration"
    Invoke-Platform $designer ("DESIGNER /F `"" + $basePath + "`" /LoadConfigFromFiles `"" + $sourcesPath + "`" /UpdateDBCfg /DisableStartupDialogs /DisableStartupMessages")

    Write-Step "Creating the users"
    New-TestUsers $basePath $testBase

    Write-Host ""
    Write-Host ("The test base is ready in " + $basePath)
    Write-Host ("Its users: " + $testBase.administrator.user + ", " + $testBase.read.user + ", " + $testBase.write.user + ". Their passwords are in " + $labPath + ".")
}
catch {
    $message = $_.Exception.GetBaseException().Message

    # An error of 1C or of .NET may quote a value it refused, so no password of lab.local.json is printed.
    if ($null -ne $testBase) {
        foreach ($account in $testBase.administrator, $testBase.read, $testBase.write) {
            $message = $message.Replace([string] $account.password, "<password>")
        }
    }

    Write-Host ""
    Write-Host ("Stopped: " + $message) -ForegroundColor Red
    Write-Host ("At line " + $_.InvocationInfo.ScriptLineNumber + " of " + $PSCommandPath)
    $scriptExitCode = 1
}
finally {
    if (Test-Path -LiteralPath $logPath) {
        Remove-Item -LiteralPath $logPath
    }
}
exit $scriptExitCode
