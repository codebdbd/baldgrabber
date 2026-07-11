
[Setup]
AppName=BaldGrabber
AppVersion=1.0.0
DefaultDirName={commonpf}\BaldGrabber
DefaultGroupName=BaldGrabber
OutputBaseFilename=BaldGrabber_Installer
Compression=lzma
SolidCompression=yes
WizardStyle=modern
DisableDirPage=yes
DisableProgramGroupPage=yes
SetupIconFile=BaldGrabber\Assets\AppIcon.ico
UninstallDisplayIcon={app}\BaldGrabber.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[Files]
Source: "BaldGrabber_Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\BaldGrabber"; Filename: "{app}\BaldGrabber.exe"
Name: "{commondesktop}\BaldGrabber"; Filename: "{app}\BaldGrabber.exe"
