# Folder Synchroization Tool

This app is a C#-based folder synchronization tool that uses Merkle trees to efficiently detect and synchronize changes between a source and a replica directory. It monitors both folders for file and directory changes using FileSystemWatcher and updates its internal tree structures. It periodically syncs the replica to match the source, logging all operations to a file and console.

### How to run

#### Build
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

#### CLI Arguments
```
-s, --srcFolder       Required. Path to the source folder to be replicated

-r, --repFolder       Required. Path to the replica folder

-i, --syncInterval    Required. Synchronization interval in milliseconds

-l, --logFile         Required. Path to the log file

--help                Display this help screen.

--version             Display version information.
```

#### Run

```
.\veeam_fold_sync\bin\Release\net9.0\win-x64\veeam_fold_sync.exe
-s "c:\path\to\folder\source\folder\"
-r "c:\path\to\folder\replica\folder\"
-i 15000
-l "c:\path\to\logfile\"
```
