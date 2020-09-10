## Folder synchroniser with file history functionality
Automatically and in realtime copies updated files from source folder to destination folder for backup purposes. It is also possible to enable bi-directional synchronisation, and/or automatic generation of history of old file versions. 

### State
Ready to use. Maintained and in active use.

### Example configuration illustrating the capabilities of the software:

	{
		"Files": {
			"SrcPath": "C:\\yourpath\\yourproject\\",

			"EnableMirror": true,
			"Bidirectional": false,
			"MirrorDestPath": "C:\\yourpath\\yourproject-backup\\",

			"EnableHistory": true,
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
			],
			"MirrorIgnorePathsContaining": [
				"\\db.lock",
				"\\Logs\\",
				"\\node_modules\\",
				"\\wwwroot\\dist\\"
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
			],
			"HistoryIgnorePathsContaining": [
				".localhistory\\",
				".vshistory\\",
				"\\bin\\",
				"\\obj\\",
				"\\db.lock",
				"\\sqlite3\\",
				"\\Logs\\",
				"\\node_modules\\",
				"\\wwwroot\\dist\\"
			]
		}
	}


### Installation

    * Copy appsettings.example.json to appsettings.json
    * Update the settings in appsettings.json according to your needs
    * Build the project
    * In the build folder launch FolderSync.bat


[![Analytics](https://ga-beacon.appspot.com/UA-351728-28/FolderSyncNet/README.md?pixel)](https://github.com/igrigorik/ga-beacon)   
    
    
