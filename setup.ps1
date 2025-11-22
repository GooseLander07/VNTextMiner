<#
.SYNOPSIS
    VNTextMiner Auto-Installer
    Downloads app/dicts, installs dependencies, and creates Desktop + Start Menu shortcuts.
#>

$ErrorActionPreference = "Stop"
$appName = "VNTextMiner"
$installDir = "$env:LOCALAPPDATA\$appName"
$desktop = [Environment]::GetFolderPath("Desktop")
$startMenu = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"

# --- CONFIGURATION URLS ---
$urlAppZip   = "https://github.com/GooseLander07/VNTextMiner/releases/download/v1.1.0/VNTextMiner_v1.1.0.zip"
$urlMeCabDic = "https://github.com/GooseLander07/VNTextMiner/releases/download/v1.1.0/NMeCab-dic.zip" 
$urlJitendex = "https://github.com/GooseLander07/VNTextMiner/releases/download/v1.1.0/jitendex.zip" 

Write-Host "=== VNTextMiner Installer ===" -ForegroundColor Cyan

# 1. Check .NET 8
Write-Host "[1/5] Checking for .NET 8..."
try {
    dotnet --list-runtimes | Select-String "Microsoft.WindowsDesktop.App 8." > $null
    Write-Host "   -> .NET 8 found." -ForegroundColor Green
} catch {
    Write-Warning "   -> .NET 8 Desktop Runtime not found! Please download it from: https://dotnet.microsoft.com/download/dotnet/8.0"
    Pause
    Exit
}

# 2. Prepare Directory
Write-Host "[2/5] Preparing Installation Directory: $installDir"
if (Test-Path $installDir) { Remove-Item -Path $installDir -Recurse -Force }
New-Item -Path $installDir -ItemType Directory | Out-Null

# 3. Download & Install App
Write-Host "[3/5] Downloading App..."
try {
    Invoke-WebRequest -Uri $urlAppZip -OutFile "$installDir\app.zip"
    Expand-Archive -Path "$installDir\app.zip" -DestinationPath $installDir -Force
    Remove-Item "$installDir\app.zip"
} catch {
    Write-Error "Failed to download App. Link might be broken."
    Pause
    Exit
}

# 4. Download Dictionaries
Write-Host "[4/5] Downloading Dictionaries..."

# A. Jitendex
Write-Host "   -> Downloading Jitendex..."
try {
    Invoke-WebRequest -Uri $urlJitendex -OutFile "$installDir\jitendex.zip"
} catch {
    Write-Warning "   -> Failed to download Jitendex."
}

# B. MeCab Dictionary (FIXED CLEANUP)
Write-Host "   -> Setting up MeCab Dictionary..."
$dicDir = "$installDir\dic"
New-Item -Path $dicDir -ItemType Directory -Force | Out-Null

try {
    $zipPath = "$dicDir\mecab-dic.zip"
    
    # 1. Download
    Invoke-WebRequest -Uri $urlMeCabDic -OutFile $zipPath
    
    # 2. Extract into 'dic' folder
    Expand-Archive -Path $zipPath -DestinationPath $dicDir -Force
    
    # 3. CLEANUP: Delete the zip file immediately
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
        Write-Host "   -> Cleaned up mecab-dic.zip" -ForegroundColor DarkGray
    }
} catch {
    Write-Warning "   -> Failed to download MeCab Dictionary."
}

# 5. Create Shortcuts (Desktop + Start Menu)
Write-Host "[5/5] Creating Shortcuts..."
try {
    $exePath = "$installDir\OverlayApp.exe"
    
    # Handle potential subfolder nesting from zip
    if (-not (Test-Path $exePath)) {
        $subItems = Get-ChildItem -Path $installDir -Directory
        if ($subItems.Count -eq 1) {
            $subPath = "$installDir\" + $subItems[0].Name + "\OverlayApp.exe"
            if (Test-Path $subPath) {
                $exePath = $subPath
                $installDir = "$installDir\" + $subItems[0].Name
            }
        }
    }

    if (Test-Path $exePath) {
        $wshShell = New-Object -ComObject WScript.Shell

        # Create Desktop Shortcut
        $shortcutDesktop = $wshShell.CreateShortcut("$desktop\$appName.lnk")
        $shortcutDesktop.TargetPath = $exePath
        $shortcutDesktop.WorkingDirectory = $installDir
        $shortcutDesktop.IconLocation = "$exePath,0"
        $shortcutDesktop.Save()

        # Create Start Menu Shortcut
        $shortcutStart = $wshShell.CreateShortcut("$startMenu\$appName.lnk")
        $shortcutStart.TargetPath = $exePath
        $shortcutStart.WorkingDirectory = $installDir
        $shortcutStart.IconLocation = "$exePath,0"
        $shortcutStart.Save()

        Write-Host "Success! Installed to Desktop and Start Menu." -ForegroundColor Green
    } else {
        Write-Error "Could not find OverlayApp.exe."
    }
} catch {
    Write-Error "Failed to create shortcuts."
}

Pause