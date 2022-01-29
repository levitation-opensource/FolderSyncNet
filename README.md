## Folder synchroniser with file history functionality
Automatically and in realtime copies updated files from source folder to destination folder for backup purposes. It is also possible to turn on bi-directional synchronisation, and/or automatic generation of version history of old file versions. 

### State
Ready to use. Maintained and in active use.

### Example configuration illustrating the capabilities of the software:

	{
		"Files": {
			"SrcPath": "C:\\yourpath\\yourproject\\",

			"MaxFileSizeMB": 2048,
			"CaseSensitiveFilenames": null,
			"DoNotCompareFileContent": false,
			"DoNotCompareFileDate": false,
			"DoNotCompareFileSize": false,
			
			"CacheDestAndHistoryFolders": false,
			"PersistentCacheDestAndHistoryFolders": false,
			"CachePath": ".\\cache\\",

			"UsePolling": false,
			"PollingDelay": 60,
			"RetryCountOnEmptyDirlist": 0,

			"UseIdlePriority": false,
			"DirlistReadDelayMs": 0,
			"FileWriteDelayMs": 0,
			"WriteBufferKB": 0,
			"BufferWriteDelayMs": 0,
			"ShowErrorAlerts": true,

			"EnableMirror": true,
			"Bidirectional": false,
			"MirrorIgnoreSrcDeletions": false,
			"MirrorIgnoreDestDeletions": false,
			"MirrorDestPath": "C:\\yourpath\\yourproject-backup\\",

			"EnableHistory": false,
			"HistoryDestPath": "C:\\yourpath\\yourproject-history\\",
			"HistoryVersionFormat": "timestamp_before_ext",
			"___VersionFormatOptions": "prefix_timestamp | timestamp_before_ext | sufix_timestamp",
			"HistoryVersionSeparator": ".",

			"MirrorWatchedExtensions": [
				"*"
			],
			"MirrorExcludedExtensions": [
				"*~",
				"tmp"
			],
			"MirrorIgnorePathsStartingWith": [
				"\\Temp\\"
			],
			"MirrorIgnorePathsContaining": [
				"\\~$",
				".tmp\\",
				"\\db.lock",
				"\\Logs\\",
				"\\node_modules\\",
				"\\wwwroot\\dist\\"
			],
			"MirrorIgnorePathsEndingWith": [
			],

			"HistoryWatchedExtensions": [
				"*"
			],
			"HistoryExcludedExtensions": [
				"*~",
				"bak",
				"tmp"
			],
			"HistoryIgnorePathsStartingWith": [
				"\\Temp\\"
			],
			"HistoryIgnorePathsContaining": [
				"\\~$",
				".tmp\\",
				".localhistory\\",
				".vshistory\\",
				"\\bin\\",
				"\\obj\\",
				"\\db.lock",
				"\\sqlite3\\",
				"\\Logs\\",
				"\\node_modules\\",
				"\\wwwroot\\dist\\"
			],
			"HistoryIgnorePathsEndingWith": [
			]
		}
	}


### Installation

    * Copy appsettings.example.json to appsettings.json
    * Update the settings in appsettings.json according to your needs
    * Build the project
    * In the build folder launch FolderSync.bat


[![Analytics](https://ga-beacon.appspot.com/UA-351728-28/FolderSyncNet/README.md?pixel)](https://github.com/igrigorik/ga-beacon)   
    
    
