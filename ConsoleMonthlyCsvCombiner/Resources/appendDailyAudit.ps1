# Monthly CSV Combiner
Write-Host "Monthly CSV Combiner" -ForegroundColor Cyan

# -------------------- FUNCTION DEFINITIONS --------------------

function New-OutputFolder {
    <#
    .SYNOPSIS
    Creates the output folder if it doesn't exist
    #>
    param(
        [string]$FolderName = "Monthly_Combined"
    )
    
    if (-not (Test-Path $FolderName)) {
        New-Item -ItemType Directory -Path $FolderName -Force | Out-Null
        Write-Host "Created output folder: $FolderName" -ForegroundColor Green
    } else {
        Write-Host "Output folder already exists: $FolderName" -ForegroundColor Gray
    }
    
    return $FolderName
}

function Get-CsvFiles {
    <#
    .SYNOPSIS
    Gets all CSV files matching the pattern
    #>
    param(
        [string]$Pattern = "dl_*.csv"
    )
    
    $files = Get-ChildItem -Filter $Pattern
    
    if (-not $files) {
        Write-Host "No files found matching pattern '$Pattern'!" -ForegroundColor Red
        return $null
    }
    
    Write-Host "Found $($files.Count) CSV files" -ForegroundColor Green
    return $files
}

function Test-DiskSpace {
    <#
    .SYNOPSIS
    Checks available disk space and estimates requirements
    #>
    param(
        [string]$OutputFolder,
        [object]$Files,
        [switch]$Force
    )
    
    # Get output drive
    $outputDrive = Split-Path -Qualifier (Resolve-Path $OutputFolder).Path
    if ($outputDrive -eq '') { 
        $outputDrive = (Get-Location).Drive.Name + ':' 
    }
    
    try {
        # Get disk info
        $diskInfo = Get-WmiObject Win32_LogicalDisk -Filter "DeviceID='$outputDrive'" -ErrorAction Stop
        
        # Calculate space metrics
        $freeSpaceGB = [math]::Round($diskInfo.FreeSpace / 1GB, 2)
        $totalSpaceGB = [math]::Round($diskInfo.Size / 1GB, 2)
        $freeSpacePercent = [math]::Round(($diskInfo.FreeSpace / $diskInfo.Size) * 100, 1)
        
        # Display disk info
        Write-Host "`nDisk Space Check:" -ForegroundColor Yellow
        Write-Host "  Drive: $outputDrive" -ForegroundColor Gray
        Write-Host "  Free Space: $freeSpaceGB GB ($freeSpacePercent%)" -ForegroundColor Gray
        Write-Host "  Total Space: $totalSpaceGB GB" -ForegroundColor Gray
        
        # Estimate space requirements
        $totalInputSize = ($Files | Measure-Object Length -Sum).Sum
        $totalInputSizeGB = [math]::Round($totalInputSize / 1GB, 2)
        $estimatedOutputSizeGB = [math]::Round($totalInputSizeGB * 0.8, 2)
        $requiredSpaceGB = [math]::Round($estimatedOutputSizeGB * 1.5, 2)
        
        Write-Host "`nSpace Requirements:" -ForegroundColor Yellow
        Write-Host "  Total Input Size: $totalInputSizeGB GB" -ForegroundColor Gray
        Write-Host "  Estimated Output Size: $estimatedOutputSizeGB GB" -ForegroundColor Gray
        Write-Host "  Recommended Free Space: $requiredSpaceGB GB" -ForegroundColor Gray
        
        # Check if space is sufficient
        if ($freeSpaceGB -lt $requiredSpaceGB) {
            Write-Host "`nWARNING: Insufficient disk space!" -ForegroundColor Red
            Write-Host "You have $freeSpaceGB GB free but need approximately $requiredSpaceGB GB" -ForegroundColor Red
            
            if (-not $Force) {
                $response = Read-Host "Continue anyway? (Y/N)"
                if ($response -notin @('Y', 'y')) {
                    return $false
                }
            }
        } else {
            Write-Host "`nDisk space check: PASSED" -ForegroundColor Green
        }
        
        return @{
            Drive = $outputDrive
            FreeSpaceGB = $freeSpaceGB
            RequiredSpaceGB = $requiredSpaceGB
        }
    } catch {
        Write-Host "`nWarning: Could not check disk space. Continuing anyway..." -ForegroundColor Yellow
        Write-Host "Error: $_" -ForegroundColor DarkYellow
        return $null
    }
}

function Group-FilesByMonth {
    <#
    .SYNOPSIS
    Groups CSV files by year-month
    #>
    param(
        [object]$Files
    )
    
    $groups = $Files | Group-Object {
        if ($_.Name -match 'dl_(\d{4})_(\d{2})_') {
            "$($matches[1])_$($matches[2])"
        }
    } | Where-Object { $_.Name } | Sort-Object Name
    
    return $groups
}

function Show-FileGroups {
    <#
    .SYNOPSIS
    Displays detailed information about file groups
    #>
    param(
        [object]$Groups
    )
    
    Write-Host "`nMonthly Groups (Detailed):" -ForegroundColor Cyan
    
    $totalFiles = 0
    foreach ($group in $Groups) {
        $year, $month = $group.Name.Split('_')
        $totalFiles += $group.Count
        Write-Host "`n$year-$month ($($group.Count) files):" -ForegroundColor Yellow
        
        $group.Group | Sort-Object Name | ForEach-Object {
            $fileSizeKB = [math]::Round($_.Length / 1KB, 2)
            Write-Host "  - $($_.Name) ($fileSizeKB KB)" -ForegroundColor Gray
        }
    }
    
    Write-Host "Total files: $totalFiles" -ForegroundColor Cyan
}

function Debug-FileGroups {
    <#
    .SYNOPSIS
    Debug function to see what's in the groups
    #>
    param(
        [object]$Groups
    )
    
    Write-Host "`nDEBUG - Groups found:" -ForegroundColor Magenta
    foreach ($group in $Groups) {
        Write-Host "Group Name: '$($group.Name)'" -ForegroundColor Yellow
        Write-Host "Group Count: $($group.Count)" -ForegroundColor Gray
        Write-Host "Files:" -ForegroundColor Gray
        $group.Group | ForEach-Object {
            Write-Host "  - $($_.Name)" -ForegroundColor Gray
        }
        Write-Host ""
    }
}

function Confirm-FileOverwrite {
    <#
    .SYNOPSIS
    Handles file overwrite confirmation
    #>
    param(
        [string]$FilePath
    )
    
    if (Test-Path $FilePath) {
        Write-Host "File exists: $FilePath" -ForegroundColor Red
        $response = Read-Host "Overwrite? (Y=Yes, N=No, S=Skip)"
        
        switch ($response.ToUpper()) {
            'Y' { return 'Overwrite' }
            'N' { return 'Cancel' }
            'S' { return 'Skip' }
            default { return 'Cancel' }
        }
    }
    
    return 'Proceed'
}

function Sort-FilesByDay {
    <#
    .SYNOPSIS
    Sorts files by day number for a month group
    #>
    param(
        [object]$FileGroup
    )
    
    $sorted = $FileGroup | Sort-Object {
        if ($_.Name -match 'dl_\d{4}_\d{2}_(\d{2})\.csv$') {
            [int]$matches[1]
        }
    }
    
    return $sorted
}

function Get-DayList {
    <#
    .SYNOPSIS
    Extracts day numbers from sorted files
    #>
    param(
        [object]$SortedFiles
    )
    
    $dayList = @()
    $SortedFiles | ForEach-Object {
        if ($_.Name -match 'dl_\d{4}_\d{2}_(\d{2})\.csv$') {
            $dayList += $matches[1]
        }
    }
    
    return $dayList
}

function Get-MonthFileStats {
    <#
    .SYNOPSIS
    Calculates statistics for a month's files
    #>
    param(
        [object]$FileGroup
    )
    
    $monthFilesSize = ($FileGroup | Measure-Object Length -Sum).Sum
    $monthFilesSizeMB = [math]::Round($monthFilesSize / 1MB, 2)
    
    return @{
        TotalSizeMB = $monthFilesSizeMB
        FileCount = $FileGroup.Count
    }
}

function Combine-MonthlyFiles {
    <#
    .SYNOPSIS
    Combines files for a single month
    #>
    param(
        [object]$SortedFiles,
        [string]$OutputFile,
        [string]$YearMonth
    )
    
    $monthStartTime = Get-Date
    $counter = 0
    $totalRowsProcessed = 0
    $year, $month = $YearMonth.Split('_')
    
    foreach ($file in $SortedFiles) {
        $counter++
        
        if ($file.Name -match 'dl_\d{4}_\d{2}_(\d{2})\.csv$') {
            $dayNum = $matches[1]
            
            # Count rows
            $fileRows = 0
            Get-Content $file.FullName | ForEach-Object { $fileRows++ }
            $dataRows = $fileRows - 1
            
            # Calculate progress
            $dayPercent = [math]::Round(($counter / $SortedFiles.Count) * 100, 1)
            $fileSizeMB = [math]::Round($file.Length / 1MB, 2)
            
            if ($counter -eq 1) {
                # First file - include header
                Write-Host "  [$counter/$($SortedFiles.Count)] $dayNum - $($file.Name) ($fileRows rows, $fileSizeMB MB)"
                Get-Content $file.FullName | Set-Content $OutputFile
                $totalRowsProcessed += $dataRows
            } else {
                # Subsequent files - skip header
                Write-Host "  [$counter/$($SortedFiles.Count)] $dayNum - $($file.Name) ($fileRows rows, $fileSizeMB MB)"
                Get-Content $file.FullName | Select-Object -Skip 1 | Add-Content $OutputFile
                $totalRowsProcessed += $dataRows
            }
            
            Write-Host "    -> Processed $dataRows data rows - $dayPercent% of month"
        }
    }
    
    return @{
        StartTime = $monthStartTime
        TotalRows = $totalRowsProcessed
        FileCount = $counter
    }
}

function Get-MonthResults {
    <#
    .SYNOPSIS
    Gets final results for a processed month
    #>
    param(
        [string]$OutputFile,
        [datetime]$StartTime,
        [int]$GroupCount,
        [int]$TotalRows
    )
    
    # Calculate output stats
    $outputRows = 0
    if (Test-Path $OutputFile) {
        Get-Content $OutputFile | ForEach-Object { $outputRows++ }
    }
    
    $monthEndTime = Get-Date
    $monthDuration = [math]::Round(($monthEndTime - $StartTime).TotalSeconds, 2)
    $outputSizeMB = [math]::Round((Get-Item $OutputFile -ErrorAction SilentlyContinue).Length / 1MB, 2)
    
    return @{
        OutputRows = $outputRows
        Duration = $monthDuration
        OutputSizeMB = $outputSizeMB
        EndTime = $monthEndTime
    }
}

function Show-MonthSummary {
    <#
    .SYNOPSIS
    Displays summary for a processed month
    #>
    param(
        [int]$GroupCount,
        [int]$TotalRows,
        [int]$OutputRows,
        [double]$OutputSizeMB,
        [double]$Duration,
        [string]$OutputFile
    )
    
    Write-Host "  -> Combined: $GroupCount files, $TotalRows data rows" -ForegroundColor Cyan
    Write-Host "  -> Saved to: $OutputFile ($OutputRows rows total, $OutputSizeMB MB)" -ForegroundColor Green
    Write-Host "  -> Month processing time: $Duration seconds" -ForegroundColor Gray
}

function Estimate-RemainingTime {
    <#
    .SYNOPSIS
    Estimates remaining processing time
    #>
    param(
        [int]$CurrentMonth,
        [int]$TotalMonths,
        [double]$CurrentDuration
    )
    
    if ($CurrentMonth -lt $TotalMonths) {
        $monthsRemaining = $TotalMonths - $CurrentMonth
        $estimatedRemainingSeconds = [math]::Round($CurrentDuration * $monthsRemaining, 0)
        
        if ($estimatedRemainingSeconds -gt 60) {
            $estimatedRemainingMinutes = [math]::Round($estimatedRemainingSeconds / 60, 1)
            Write-Host "  -> Estimated time remaining: $estimatedRemainingMinutes minutes" -ForegroundColor Gray
        } else {
            Write-Host "  -> Estimated time remaining: $estimatedRemainingSeconds seconds" -ForegroundColor Gray
        }
    }
}

function Show-FinalSummary {
    <#
    .SYNOPSIS
    Displays final summary after all processing
    #>
    param(
        [int]$TotalMonths,
        [int]$TotalFiles,
        [string]$OutputFolder,
        [hashtable]$DiskInfo,
        [double]$InitialFreeSpace
    )
    
    Write-Host "`n" + ("=" * 50) -ForegroundColor DarkGray
    Write-Host "PROCESSING COMPLETE" -ForegroundColor Green
    Write-Host "=" * 50 -ForegroundColor DarkGray
    Write-Host "Total months processed: $TotalMonths" -ForegroundColor Cyan
    Write-Host "Total files processed: $TotalFiles" -ForegroundColor Cyan
    
    # Check final disk space if initial info was available
    if ($DiskInfo) {
        try {
            $finalDiskInfo = Get-WmiObject Win32_LogicalDisk -Filter "DeviceID='$($DiskInfo.Drive)'"
            $finalFreeSpaceGB = [math]::Round($finalDiskInfo.FreeSpace / 1GB, 2)
            $spaceUsedGB = [math]::Round($DiskInfo.FreeSpaceGB - $finalFreeSpaceGB, 2)
            
            Write-Host "`nFinal Disk Status:" -ForegroundColor Yellow
            Write-Host "  Space used by operation: $spaceUsedGB GB" -ForegroundColor Gray
            Write-Host "  Remaining free space: $finalFreeSpaceGB GB" -ForegroundColor Gray
            
            if ($finalFreeSpaceGB -lt 1) {
                Write-Host "  WARNING: Less than 1GB free space remaining!" -ForegroundColor Red
            }
        } catch {
            Write-Host "`nNote: Final disk space check skipped" -ForegroundColor DarkYellow
        }
    }
    
    Write-Host "`nAll files saved to: $((Resolve-Path $OutputFolder).Path)" -ForegroundColor Green
    Write-Host "`nDone!" -ForegroundColor Green
}

# -------------------- MAIN SCRIPT EXECUTION --------------------

# Step 1: Setup
$outputFolder = New-OutputFolder

# Step 2: Get files
$files = Get-CsvFiles
if (-not $files) { exit }

# Step 3: Check disk space
$diskInfo = Test-DiskSpace -OutputFolder $outputFolder -Files $files
if ($diskInfo -eq $false) {
    Write-Host "Operation cancelled." -ForegroundColor Yellow
    exit
}

# Step 4: Group files by month
$groups = Group-FilesByMonth -Files $files
if (-not $groups) {
    Write-Host "No valid monthly groups found!" -ForegroundColor Red
    exit
}

# Step 5: Show file groups
Show-FileGroups -Groups $groups

# Step 6: Process each month
$monthCounter = 0
foreach ($group in $groups) {
    $monthCounter++
    $year, $month = $group.Name.Split('_')
    $outputFile = "$outputFolder\montly_${year}_${month}.csv"
    
    # Show progress
    $percentComplete = [math]::Round(($monthCounter - 1) / $groups.Count * 100, 1)
    Write-Host "`n[$monthCounter/$($groups.Count)] $year-$month ($($group.Count) files)" -ForegroundColor Yellow
    Write-Host "Progress: $percentComplete% complete" -ForegroundColor Gray
    
    # Check for existing file
    $overwriteResult = Confirm-FileOverwrite -FilePath $outputFile
    if ($overwriteResult -eq 'Cancel') { exit }
    if ($overwriteResult -eq 'Skip') { continue }
    
    # Sort files by day
    $sortedFiles = Sort-FilesByDay -FileGroup $group.Group
    
    # Show day order
    $dayList = Get-DayList -SortedFiles $sortedFiles
    Write-Host "Days: $($dayList -join ' ')"
    
    # Get month file statistics
    $monthStats = Get-MonthFileStats -FileGroup $group.Group
    Write-Host "Month data size: $($monthStats.TotalSizeMB) MB" -ForegroundColor Gray
    
    # Combine files for this month
    $combineResult = Combine-MonthlyFiles -SortedFiles $sortedFiles -OutputFile $outputFile -YearMonth $group.Name
    
    # Get results
    $monthResults = Get-MonthResults -OutputFile $outputFile -StartTime $combineResult.StartTime -GroupCount $group.Count -TotalRows $combineResult.TotalRows
    
    # Show month summary
    Show-MonthSummary -GroupCount $group.Count -TotalRows $combineResult.TotalRows -OutputRows $monthResults.OutputRows -OutputSizeMB $monthResults.OutputSizeMB -Duration $monthResults.Duration -OutputFile $outputFile
    
    # Estimate remaining time
    Estimate-RemainingTime -CurrentMonth $monthCounter -TotalMonths $groups.Count -CurrentDuration $monthResults.Duration
}

# Step 7: Final summary
$initialFreeSpace = if ($diskInfo) { $diskInfo.FreeSpaceGB } else { 0 }
Show-FinalSummary -TotalMonths $groups.Count -TotalFiles $files.Count -OutputFolder $outputFolder -DiskInfo $diskInfo -InitialFreeSpace $initialFreeSpace