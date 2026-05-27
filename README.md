# Description
A console application that combines daily CSV files into monthly files. The tool scans a source directory for CSV files matching a configurable pattern <u>(e.g., <mark>dl_YYYY_MM_DD.csv</mark>)</u>, groups them by year‑month, and produces one aggregated CSV file per month.

<!-- Badge showing workflow status -->
[![Build and Release](https://github.com/NaserKhoshfetrat/CsvCombiner/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/NaserKhoshfetrat/CsvCombiner/actions/workflows/dotnet-desktop.yml)

## Download the latest build

[![Download latest artifact](https://img.shields.io/badge/download-latest_build-blue)](https://nightly.link/NaserKhoshfetrat/CsvCombiner/workflows/dotnet-desktop/main/ConsoleMonthlyCsvCombiner.zip)

> **Note:** The download link above always points to the most recent successful build from the `main` branch.  
> No GitHub account is required – the file is served via [nightly.link](https://nightly.link).


# Features
* [x] **Monthly aggregation** – Automatically groups files by year and month.
* [x] **Flexible source & output** – User can specify the input folder and output destination via command line or configuration file.
* [x] **Intelligent defaults** – If no output folder is provided, files are saved to Monthly_Combined inside the executable's directory.
* [x] **Overwrite protection** – Prompts the user before overwriting existing monthly files (Yes / Skip / Cancel).
* [x] **Disk space check** – Warns if available free space is insufficient.
* [x] **Progress reporting** – Shows detailed logs and estimated remaining time.
* [x] **Cross‑process safety** – Uses named mutexes to prevent concurrent writes to the same output file.

# Requirements
.NET 10.0 or later.

Input CSV files must follow the naming pattern:
{prefix}{YYYY}_{MM}_{DD}.csv
(e.g., dl_2025_03_01.csv).
The tool extracts the date from the filename; the file content is not inspected for dates.

# license
MIT

# Usage
Run the executable from the command line, optionally providing parameters.

# Command line arguments
| Argument  | Alias          | Description                                                                                                   | Required |
|-----------|----------------|---------------------------------------------------------------------------------------------------------------|----------|
| --source  | --SourceFolder | Path to the folder containing the daily CSV files.                                                            | No       |
| --output  | --OutputFolder | Folder where monthly CSV files will be saved. Defaults to Monthly_Combined inside the executable’s directory. | No       |
| --pattern | --FilePattern  | File search pattern (supports * wildcard). Defaults to dl_*.csv.                                              | No       |


>Note: Arguments can also be specified in an appsettings.json file (see Configuration below). Command line arguments take precedence.

# Examples

## Basic usage (output defaults to exe folder)

`MonthlyCsvCombiner.exe --source "C:\Data\Incoming"`
>This reads all dl_*.csv from C:\Data\Incoming and writes monthly files to C:\Path\To\Exe\Monthly_Combined.


## Specify both source and output
`MonthlyCsvCombiner.exe --source "C:\Data\Incoming" --output "D:\Reports\Monthly"
`

## Use a custom file pattern
`MonthlyCsvCombiner.exe --source "C:\Data\Incoming" --pattern "sales_*.csv"`

## Using appsettings.json and overriding only the source

`MonthlyCsvCombiner.exe --source "E:\Temp\CSVs"`

# Configuration
Create an appsettings.json file in the same directory as the executable. Example:

```json
{
  "SourceFolder": "C:\\DefaultSource",
  "OutputFolder": "Monthly_Combined",
  "FilePattern": "dl_*.csv"
}
```

<small>Any settings provided via command line arguments will override the values in appsettings.json.</small>


# Input File Format
* Each daily CSV file must contain a header row.
* The filename must match the pattern <mark>**prefixYYYY_MM_DD.csv**</mark> (e.g., dl_2025_03_01.csv).
* The tool combines files by:
    * Including the header only from the first file of each month.
    * Appending all data rows from all files of that month (excluding headers from subsequent files).
  

# Example

dl_2025_03_01.csv

```text
Date,Value
2025-03-01,10
2025-03-01,20
```

dl_2025_03_02.csv

```text

Date,Value
2025-03-02,30
```

Resulting monthly_2025_03.csv

```text

Date,Value
2025-03-01,10
2025-03-01,20
2025-03-02,30
```

# Output
* [ ] Monthly files are named monthly_{YYYY}_{MM}.csv (e.g., monthly_2025_03.csv).
* [ ] They are saved in the specified output folder (or the default).
* [ ] A log file log.txt is created in the working directory for troubleshooting.

# Error Handling & Prompts
**Overwrite prompt** – If a monthly output file already exists, the user is asked:
Overwrite? (Y=Yes, N=No, S=Skip)

**Disk space warning** – If free space is less than 1.5× the estimated output size, the user is asked:
Continue anyway? (Y/N)

**No matching files** – The program logs an error and exits gracefully.

# Logging
* Console output with timestamps and log levels.
* A rolling file log (log.txt) is created for debugging.

# Installation
* Clone or download the repository.
* Build the project using your IDE or the .NET CLI
The executable will be located in bin/Release/net10.0/.

## Automated Build Script

A convenience script `publish.bat` is provided in the `Resources` folder. It publishes the application as a single, self‑contained executable.

### Usage

From the `Resources` folder, run:

```bash
publish.bat
```
### Publish requirements
* .NET SDK 10.0 or later.
* Windows (the script uses `@echo off` and powershell for hashing).
>For cross‑platform, adapt the script accordingly.

#### Notes
The script assumes the project is located one directory level above Resources.

You can also publish manually using the `dotnet publish` command.

# Building from Source

1. Ensure .NET 10.0 SDK is installed.

2. Restore dependencies:

```bash

dotnet restore
```
3. Build:

```bash
dotnet build -c Release
```
4. Run tests:
```bash
dotnet test
```
## Creating a publish profile (optional)

If you prefer using a Visual Studio publish profile instead of the command line or `publish.bat`, you can create a `.pubxml` file inside the `Properties/PublishProfiles/` folder.  

Use the following content as a template (do **not** commit this file if your `.gitignore` excludes `*.pubxml`):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>Any CPU</Platform>
    <PublishDir>..\Resources\dist\</PublishDir>
    <PublishProtocol>FileSystem</PublishProtocol>
    <_TargetId>Folder</_TargetId>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <DebugType>None</DebugType>
    <DebugSymbols>false</DebugSymbols>
    <PublishReadyToRun>false</PublishReadyToRun>
    <PublishTrimmed>false</PublishTrimmed>
  </PropertyGroup>
</Project>
```

Once the profile is saved (e.g., FolderProfile.pubxml), you can publish by right‑clicking the project in Visual Studio and selecting Publish.


<div style='page-break-after: always'></div>

# PowerShell Alternative
If you prefer a script‑based solution, a **PowerShell script** (`appendDailyAudit.ps1`) is included in the `Resources` folder.  
It provides similar monthly CSV combining functionality and can be used independently of the compiled executable.
### Quick start with PowerShell
```powershell
.\appendDailyAudit.ps1
```
The script do not accepts the same parameters as the console application (-SourceFolder, -OutputFolder, -Pattern) and is especially useful for automation or environments where running executable is restricted.


# Support
For issues or feature requests, please open an issue in the project repository.

