# PS4RPI Reloaded

PS4RPI Reloaded is a fork of the original / awesome PS4RPI made by sonic-br (https://github.com/sonik-br/PS4RPIGUI)
I have made a several enhancement on top of his work.. 

### Support for Batch Installation
Version 3.0 supports batch installation of packages. From the list box, select multiple packages and clikc “Install Selected PKGs” button, it’ll start sending package files to PS4, and each package transfer will be tracked in a separate progress bar. 
Note: Do not close PS4RPI application nor send “Remote Package Installer” to the background on the PS4 side. Due to the bug in PS4’s remote package installer, batch install will fail! 

### Health Check of PS4 Remote Package Installer
Now, PS4 check button, not only ping your PS4 system, it checks the health state of PS4 Remote Installer. I’ve noticed PS4 RPI goes into weird limbo state once it’s backgrounded ☹. You’ll need to completely close (option->close) app and relaunch the PS4 RPI. PS4 Check button will reveal PS4 RPI health state. 
 
### Using hardlink name to transfer
With the 3.0 utilizes the hardlinks instead of the physical files, thus file name can contain any special characters, spaces, and non-alpha-numeric characters. PS4 remote install will just work. No more internal server errors or pre-requisite failure!  

Note1: If you’re using exFAT or FAT32 drive, can’t use this feature. Hardlink only works on NTFS file system. For exFat or FAT32 drive, rename package files to the safer names (use PS4-PKG-Tool: manage and perform various operations on PS4 PKG) for bulk renaming. 
 
Note2: Your package files MUST be from a single drive location, due to limitation of internal HTTP server, it can’t host files stored other than the drive it was specified in settings directory. 
For example, you can download files reside in E:\ drive, but can’t download C or D drive. To be able to download from any other than E drive, you can create symbolic junction to E drive to load. Or re-launch PS4RPI Reloaded, changing PKG Folder to the other drive. 

### Clean up your file name with File Shampooo
You can clean up your pkg files without worrying too much for transfer failure thanks to hardlink transfer. You now have ‘Shampoo’ option to rename all your files to cleaner, more intuitive format. Use shampoo, especially u want to manage your package files with the LaunchBox. After the shampoo, LB will import your package nicely. 
 
After Shampoo File Name, pkg files are cleaned and renamed automatically to the format of Title [TitleID][Type][Version]. 
 

### Drag and Drop to find all related packages
Drag a game or patch, PS4RPI Reloaded will search and find all of the related files, game, patch and DLCs that shares the same TitleID. Note that this search is done by the file name. (Opening up each file to parse title id takes long). Use file shampoo feature to pre-clean your package pool. 
Download order is Game first -> then Patch -> followed by DLCs/Themes/Addon. 

### Launchbox Integration
Utilizing the feature that could load the all related packges, PS4RPI Reloaded can be integrated with launchbox for you to further manage your packages neatly. Register PS4RPI as an emulator and pass ‘/lb’ option, it takes a pkg file as a parameter and pre-load all related packages for you.. 
 
