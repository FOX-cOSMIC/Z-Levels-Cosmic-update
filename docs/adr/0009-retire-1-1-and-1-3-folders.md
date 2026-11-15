# The 1.1 and 1.3 folders are retired

`ZLevels/1.1` and `ZLevels/1.3` are removed from the tree. They held four DLLs and no sources. `ZLevels/LoadFolders.xml` now loads `Common` and `1.7` only. Its `1.2` entry pointed at no folder, so it is removed too. The history stays in git. The last commit that holds the folders is `e1b58c2`, so `git show e1b58c2:ZLevels/1.1/Assemblies/ZLevels.dll` returns the retired file.

Decided 2026-11-05. This answers D7.
