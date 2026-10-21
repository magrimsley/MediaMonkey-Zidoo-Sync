using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;

// Built-in Windows Installer authoring; no PowerShell or custom actions in the MSI.
if(!OperatingSystem.IsWindows() || args.Length!=3)throw new ArgumentException("MsiBuilder <payload> <output.msi> <numeric-msi-version>");
var source=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);var version=Version.Parse(args[2]);
if(version.Revision>=0 || version.Major>255 || version.Minor>255 || version.Build is <0 or >65535)throw new ArgumentException("Use three MSI version fields within Windows limits.");
if(output.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Output must be outside payload.");
if(File.Exists(output))throw new IOException("Output already exists.");
var app=FileVersionInfo.GetVersionInfo(Path.Combine(source,"MMZidooSync.exe")).ProductVersion?.Split('+')[0]??throw new InvalidDataException("Payload version missing.");
var files=Directory.GetFiles(source,"*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();
if(files.Any(p=>new[]{".ps1",".psm1",".psd1",".ps1xml",".pssc",".psrc"}.Contains(Path.GetExtension(p).ToLowerInvariant())))throw new InvalidDataException("Scripts refused.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);var work=Path.Combine(Path.GetDirectoryName(output)!,"msi-build-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
string Id(string text)=>new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("MMZidooSync-MSI:"+text)).AsSpan(0,16)).ToString("B").ToUpperInvariant();
const string upgrade="{7DCA6B92-8BA4-40C0-923B-FC42F68E2389}";var product=Id("product:"+version);
var ddf=new StringBuilder(".OPTION EXPLICIT\n.Set Cabinet=on\n.Set Compress=on\n.Set CompressionType=MSZIP\n.Set CabinetNameTemplate=payload.cab\n.Set DiskDirectoryTemplate=\""+work+"\"\n.Set MaxDiskSize=0\n.Set MaxCabinetSize=0\n");
for(int i=0;i<files.Length;i++)ddf.AppendLine("\""+files[i]+"\" F"+(i+1).ToString("D5"));
var ddfPath=Path.Combine(work,"payload.ddf");File.WriteAllText(ddfPath,ddf.ToString());
var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"makecab.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=work};start.ArgumentList.Add("/F");start.ArgumentList.Add(ddfPath);
using(var p=Process.Start(start)!){p.WaitForExit();if(p.ExitCode!=0)throw new IOException("Cabinet build failed.");}
dynamic engine=Activator.CreateInstance(Type.GetTypeFromProgID("WindowsInstaller.Installer")!)!;dynamic db=engine.OpenDatabase(output,3);
void Sql(string sql){dynamic v=db.OpenView(sql);try{v.Execute(null);}finally{v.Close();Marshal.FinalReleaseComObject(v);}}
void Row(string table,string columns,params object?[] values){dynamic v=db.OpenView("INSERT INTO `"+table+"` ("+string.Join(",",columns.Split(',').Select(c=>"`"+c+"`"))+") VALUES ("+string.Join(",",values.Select(_=>"?"))+")");dynamic r=engine.CreateRecord(values.Length);try{for(int i=0;i<values.Length;i++){if(values[i] is int n)r.IntegerData[i+1]=n;else if(values[i]!=null)r.StringData[i+1]=values[i]!.ToString();}v.Execute(r);}finally{v.Close();Marshal.FinalReleaseComObject(r);Marshal.FinalReleaseComObject(v);}}
void Table(string name,string columns,string keys)=>Sql("CREATE TABLE `"+name+"` ("+columns+" PRIMARY KEY "+keys+")");
Table("Property","`Property` CHAR(72) NOT NULL, `Value` CHAR(0) NOT NULL LOCALIZABLE","`Property`");
Table("Directory","`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE","`Directory`");
Table("Component","`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72)","`Component`");
Table("Feature","`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL","`Feature`");
Table("FeatureComponents","`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL","`Feature_`, `Component_`");
Table("File","`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` LONG NOT NULL","`File`");
Table("Media","`DiskId` SHORT NOT NULL, `LastSequence` LONG NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72)","`DiskId`");
Table("Registry","`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL","`Registry`");
Table("Icon","`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL","`Name`");
Table("Shortcut","`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) LOCALIZABLE, `Arguments` CHAR(255) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72)","`Shortcut`");
Table("Upgrade","`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL","`UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`");
Table("LaunchCondition","`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE","`Condition`");
// Native Windows Installer options; silent/basic installs use DESKTOPSHORTCUT=1 or 0.
Table("TextStyle","`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, `Color` LONG, `StyleBits` SHORT","`TextStyle`");
Table("Dialog","`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, `Control_First` CHAR(50), `Control_Default` CHAR(50), `Control_Cancel` CHAR(50)","`Dialog`");
Table("Control","`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Property` CHAR(72), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE","`Dialog_`, `Control`");
Table("CheckBox","`Property` CHAR(72) NOT NULL, `Value` CHAR(64)","`Property`");
Table("ControlEvent","`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Argument` CHAR(255), `Condition` CHAR(255), `Ordering` SHORT","`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`");
Table("Binary","`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL","`Name`");
Table("EventMapping","`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Attribute` CHAR(50) NOT NULL","`Dialog_`, `Control_`, `Event`");
Table("ActionText","`Action` CHAR(72) NOT NULL, `Description` CHAR(64) LOCALIZABLE, `Template` CHAR(128) LOCALIZABLE","`Action`");
Row("TextStyle","TextStyle,FaceName,Size","InstallerFont","Segoe UI",9);
Row("Dialog","Dialog,HCentering,VCentering,Width,Height,Attributes,Title,Control_First,Control_Default,Control_Cancel","InstallOptions",50,50,370,180,3,"[ProductName] Setup","DesktopChoice","Install","Cancel");
Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text","InstallOptions","Heading","Text",20,15,285,30,3,"Install MM Zidoo Sync for this Windows user.");
Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text","InstallOptions","Description","Text",20,48,330,35,3,"A Start menu entry is included. Choose whether to add a desktop shortcut.");
Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Property,Text,Control_Next","InstallOptions","DesktopChoice","CheckBox",20,95,330,20,3,"DESKTOPSHORTCUT","Create a desktop shortcut","Install");
Row("CheckBox","Property,Value","DESKTOPSHORTCUT","1");
Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text,Control_Next","InstallOptions","Install","PushButton",190,140,75,23,3,"Install","Cancel");
Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text,Control_Next","InstallOptions","Cancel","PushButton",275,140,75,23,3,"Cancel","DesktopChoice");
Row("ControlEvent","Dialog_,Control_,Event,Argument,Condition,Ordering","InstallOptions","Install","EndDialog","Return","1",1);
Row("ControlEvent","Dialog_,Control_,Event,Argument,Condition,Ordering","InstallOptions","Cancel","EndDialog","Exit","1",1);
// Modeless progress receives native Windows Installer progress events during ExecuteAction.
Row("Dialog","Dialog,HCentering,VCentering,Width,Height,Attributes,Title,Control_First,Control_Default,Control_Cancel","InstallProgress",50,50,370,180,1,"[ProductName] Setup","Cancel","Cancel","Cancel");
Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text","InstallProgress","Heading","Text",20,15,285,30,3,"Setup is applying your changes.");
Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text","InstallProgress","Status","Text",20,65,330,25,3,"Preparing installation...");
Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes","InstallProgress","Progress","ProgressBar",20,100,330,15,65537);
Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text,Control_Next","InstallProgress","Cancel","PushButton",275,140,75,23,3,"Cancel","Cancel");
Row("ControlEvent","Dialog_,Control_,Event,Argument,Condition,Ordering","InstallProgress","Cancel","EndDialog","Exit","1",1);
Row("EventMapping","Dialog_,Control_,Event,Attribute","InstallProgress","Progress","SetProgress","Progress");
Row("EventMapping","Dialog_,Control_,Event,Attribute","InstallProgress","Status","ActionText","Text");
foreach(var action in new[]{("InstallFiles","Copying application files..."),("RemoveFiles","Removing application files..."),("CreateShortcuts","Creating your shortcuts..."),("RemoveShortcuts","Removing shortcuts..."),("WriteRegistryValues","Saving application registration..."),("RegisterProduct","Registering MM Zidoo Sync..."),("PublishProduct","Finishing application registration..."),("InstallFinalize","Completing setup...")})
    Row("ActionText","Action,Description",action.Item1,action.Item2);
foreach(var exit in new[]{("SetupComplete","Setup completed successfully.","Choose Finish to close setup. Open MM Zidoo Sync from the Start menu after installation."),
    ("SetupCancelled","You cancelled setup.","Setup did not complete. You can run this installer again when you are ready."),
    ("SetupFailed","Setup could not complete.","Windows Installer reported an error. Review the error message or installation log, then try again.")})
{
    Row("Dialog","Dialog,HCentering,VCentering,Width,Height,Attributes,Title,Control_First,Control_Default,Control_Cancel",exit.Item1,50,50,370,180,3,"[ProductName] Setup","Finish","Finish","Finish");
    Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text",exit.Item1,"Heading","Text",20,15,285,30,3,exit.Item2);
    Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text",exit.Item1,"Description","Text",20,65,330,55,3,exit.Item3);
    Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text,Control_Next",exit.Item1,"Finish","PushButton",275,140,75,23,3,"Finish","Finish");
    Row("ControlEvent","Dialog_,Control_,Event,Argument,Condition,Ordering",exit.Item1,"Finish","EndDialog","Return","1",1);
}
foreach(var dialog in new[]{"InstallOptions","InstallProgress","SetupComplete","SetupCancelled","SetupFailed"})
    Row("Control","Dialog_,Control,Type,X,Y,Width,Height,Attributes,Text",dialog,"BrandIcon","Icon",318,10,32,32,5242881,"InstallerIcon");
foreach(var table in new[]{"InstallExecuteSequence","InstallUISequence"})Table(table,"`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT","`Action`");
foreach(var p in new Dictionary<string,string>{["ProductCode"]=product,["UpgradeCode"]=upgrade,["ProductName"]="MM Zidoo Sync "+app,["ProductVersion"]=version.ToString(),["ProductLanguage"]="1033",["Manufacturer"]="Michael Grimsley",["INSTALLLEVEL"]="1",["MSIINSTALLPERUSER"]="1",["DESKTOPSHORTCUT"]="1",["DefaultUIFont"]="InstallerFont",["ARPNOMODIFY"]="1",["ARPPRODUCTICON"]="Application.ico",["ARPURLINFOABOUT"]="https://github.com/magrimsley/MediaMonkey-Zidoo-Sync",["ARPCOMMENTS"]="Application "+app+"; installer revision "+version+". Independent project; unsigned.",["SecureCustomProperties"]="OLDPRODUCTS;NEWERFOUND;INSTALLFOLDER;NOSHORTCUTS;DESKTOPSHORTCUT"})Row("Property","Property,Value",p.Key,p.Value);
void Dir(string id,string? parent,string name)=>Row("Directory","Directory,Directory_Parent,DefaultDir",id,parent,name);
Dir("TARGETDIR",null,"SourceDir");Dir("LocalAppDataFolder","TARGETDIR",".");Dir("ProgramsRoot","LocalAppDataFolder","Programs");Dir("INSTALLFOLDER","ProgramsRoot","MMZS|MMZidooSync");Dir("ProgramMenuFolder","TARGETDIR",".");Dir("AppMenu","ProgramMenuFolder","MMZS|MM Zidoo Sync");Dir("DesktopFolder","TARGETDIR",".");
Row("Feature","Feature,Title,Level,Attributes","Main","MM Zidoo Sync",1,0);
var dirs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){[""]="INSTALLFOLDER"};int next=0;
string FindDir(string relative){if(dirs.TryGetValue(relative,out var id))return id;var parent=FindDir(Path.GetDirectoryName(relative)??"");id="D"+(++next).ToString("D5");dirs[relative]=id;Dir(id,parent,"d"+next+"|"+Path.GetFileName(relative));return id;}
string? exe=null;
for(int i=0;i<files.Length;i++){var rel=Path.GetRelativePath(source,files[i]);var dir=FindDir(Path.GetDirectoryName(rel)??"");var f="F"+(i+1).ToString("D5");var c="C"+(i+1).ToString("D5");Row("Component","Component,ComponentId,Directory_,Attributes,KeyPath",c,Id("file:"+rel.ToLowerInvariant()),dir,256,f);Row("FeatureComponents","Feature_,Component_","Main",c);var vi=FileVersionInfo.GetVersionInfo(files[i]);// MSI requires the numeric fixed-file version, not the free-form display string.
string? fv=string.IsNullOrEmpty(vi.FileVersion)?null:$"{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}.{vi.FilePrivatePart}";Row("File","File,Component_,FileName,FileSize,Version,Attributes,Sequence",f,c,"f"+(i+1)+"|"+Path.GetFileName(files[i]),checked((int)new FileInfo(files[i]).Length),fv,16384,i+1);if(rel.Equals("MMZidooSync.exe",StringComparison.OrdinalIgnoreCase))exe=f;}
if(exe is null)throw new InvalidDataException("Missing executable.");
Row("Component","Component,ComponentId,Directory_,Attributes,Condition,KeyPath","Shortcuts",Id("shortcuts"),"INSTALLFOLDER",260,"NOT NOSHORTCUTS","ShortcutMarker");Row("FeatureComponents","Feature_,Component_","Main","Shortcuts");Row("Registry","Registry,Root,Key,Name,Value,Component_","ShortcutMarker",1,@"Software\MMZidooSync\MSI","Shortcuts",app,"Shortcuts");
Row("Component","Component,ComponentId,Directory_,Attributes,Condition,KeyPath","DesktopShortcutComponent",Id("desktop-shortcut"),"INSTALLFOLDER",260,"NOT NOSHORTCUTS AND DESKTOPSHORTCUT=\"1\"","DesktopMarker");
Row("FeatureComponents","Feature_,Component_","Main","DesktopShortcutComponent");
Row("Registry","Registry,Root,Key,Name,Value,Component_","DesktopMarker",1,@"Software\MMZidooSync\MSI","DesktopShortcut",app,"DesktopShortcutComponent");
Row("Shortcut","Shortcut,Directory_,Name,Component_,Target,Description,ShowCmd,WkDir","StartShortcut","AppMenu","MMZidoo|MM Zidoo Sync","Shortcuts","[#"+exe+"]","MediaMonkey to Zidoo playlist synchronization",1,"INSTALLFOLDER");
Row("Shortcut","Shortcut,Directory_,Name,Component_,Target,Description,ShowCmd,WkDir","DesktopShortcut","DesktopFolder","MMZidoo|MM Zidoo Sync (MSI)","DesktopShortcutComponent","[#"+exe+"]","MediaMonkey to Zidoo playlist synchronization",1,"INSTALLFOLDER");
Row("Media","DiskId,LastSequence,Cabinet",1,files.Length,"#payload.cab");Row("Upgrade","UpgradeCode,VersionMin,VersionMax,Attributes,ActionProperty",upgrade,"0.0.0",version.ToString(),257,"OLDPRODUCTS");Row("Upgrade","UpgradeCode,VersionMin,Attributes,ActionProperty",upgrade,version.ToString(),258,"NEWERFOUND");Row("LaunchCondition","Condition,Description","NOT NEWERFOUND OR Installed","A newer or equal installer revision is installed. Use its installer to repair or uninstall.");Row("LaunchCondition","Condition,Description","VersionNT64","64-bit Windows is required.");
var actions=new(string,int,string?)[]{("FindRelatedProducts",25,null),("LaunchConditions",100,null),("CostInitialize",800,null),("FileCost",900,null),("CostFinalize",1000,null),("MigrateFeatureStates",1200,null),("InstallValidate",1400,null),("InstallInitialize",1500,null),("ProcessComponents",1600,null),("UnpublishFeatures",1800,null),("RemoveShortcuts",3200,null),("RemoveFiles",3500,null),("InstallFiles",4000,null),("CreateShortcuts",4500,null),("WriteRegistryValues",5000,null),("RemoveRegistryValues",5100,null),("RegisterUser",6000,null),("RegisterProduct",6100,null),("PublishFeatures",6300,null),("PublishProduct",6400,null),("InstallExecute",6500,null),("RemoveExistingProducts",6550,"OLDPRODUCTS"),("InstallFinalize",6600,null)};
foreach(var a in actions)Row("InstallExecuteSequence","Action,Condition,Sequence",a.Item1,a.Item3,a.Item2);foreach(var a in actions.Where(a=>a.Item2<1400))Row("InstallUISequence","Action,Condition,Sequence",a.Item1,a.Item3,a.Item2);Row("InstallUISequence","Action,Sequence","ExecuteAction",1300);Row("InstallUISequence","Action,Condition,Sequence","InstallOptions","UILevel >= 5 AND NOT Installed AND NOT REMOVE AND NOT NOSHORTCUTS",200);
Row("InstallUISequence","Action,Condition,Sequence","InstallProgress","UILevel >= 5",1290);
Row("InstallUISequence","Action,Condition,Sequence","SetupComplete","UILevel >= 5",-1);
Row("InstallUISequence","Action,Condition,Sequence","SetupCancelled","UILevel >= 5",-2);
Row("InstallUISequence","Action,Condition,Sequence","SetupFailed","UILevel >= 5",-3);
// Windows Installer caches this icon for Programs and Features independently of the payload.
var iconPath=Path.Combine(source,"Resources","Branding","Application.ico");
if(!File.Exists(iconPath))throw new InvalidDataException("Missing application icon.");
dynamic artView=db.OpenView("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)");
dynamic artRecord=engine.CreateRecord(2);
try{artRecord.StringData[1]="InstallerIcon";artRecord.SetStream(2,iconPath);artView.Execute(artRecord);}
finally{artView.Close();Marshal.FinalReleaseComObject(artRecord);Marshal.FinalReleaseComObject(artView);}
dynamic iconView=db.OpenView("INSERT INTO `Icon` (`Name`, `Data`) VALUES (?, ?)");
dynamic iconRecord=engine.CreateRecord(2);
try{iconRecord.StringData[1]="Application.ico";iconRecord.SetStream(2,iconPath);iconView.Execute(iconRecord);}
finally{iconView.Close();Marshal.FinalReleaseComObject(iconRecord);Marshal.FinalReleaseComObject(iconView);}
dynamic stream=db.OpenView("INSERT INTO `_Streams` (`Name`, `Data`) VALUES (?, ?)");dynamic streamRecord=engine.CreateRecord(2);streamRecord.StringData[1]="payload.cab";streamRecord.SetStream(2,Path.Combine(work,"payload.cab"));stream.Execute(streamRecord);stream.Close();Marshal.FinalReleaseComObject(streamRecord);Marshal.FinalReleaseComObject(stream);
dynamic summary=db.SummaryInformation(12);summary.Property[2]="Installation Database";summary.Property[3]="MM Zidoo Sync "+app;summary.Property[4]="Michael Grimsley";summary.Property[7]="x64;1033";summary.Property[9]=Guid.NewGuid().ToString("B").ToUpperInvariant();summary.Property[14]=500;summary.Property[15]=10;summary.Property[19]=2;summary.Persist();db.Commit();Marshal.FinalReleaseComObject(summary);Marshal.FinalReleaseComObject(db);Marshal.FinalReleaseComObject(engine);
Console.WriteLine($"Built {output}; app={app}; MSI={version}; product={product}; files={files.Length}");



