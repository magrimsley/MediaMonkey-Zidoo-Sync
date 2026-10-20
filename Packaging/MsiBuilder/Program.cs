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
Table("Shortcut","`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) LOCALIZABLE, `Arguments` CHAR(255) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72)","`Shortcut`");
Table("Upgrade","`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL","`UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`");
Table("LaunchCondition","`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE","`Condition`");
foreach(var table in new[]{"InstallExecuteSequence","InstallUISequence"})Table(table,"`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT","`Action`");
foreach(var p in new Dictionary<string,string>{["ProductCode"]=product,["UpgradeCode"]=upgrade,["ProductName"]="MM Zidoo Sync "+app,["ProductVersion"]=version.ToString(),["ProductLanguage"]="1033",["Manufacturer"]="Michael Grimsley",["INSTALLLEVEL"]="1",["MSIINSTALLPERUSER"]="1",["LIMITUI"]="1",["ARPNOMODIFY"]="1",["ARPURLINFOABOUT"]="https://github.com/magrimsley/MediaMonkey-Zidoo-Sync",["ARPCOMMENTS"]="Application "+app+"; installer revision "+version+". Independent project; unsigned.",["SecureCustomProperties"]="OLDPRODUCTS;NEWERFOUND;INSTALLFOLDER;NOSHORTCUTS"})Row("Property","Property,Value",p.Key,p.Value);
void Dir(string id,string? parent,string name)=>Row("Directory","Directory,Directory_Parent,DefaultDir",id,parent,name);
Dir("TARGETDIR",null,"SourceDir");Dir("LocalAppDataFolder","TARGETDIR",".");Dir("ProgramsRoot","LocalAppDataFolder","Programs");Dir("INSTALLFOLDER","ProgramsRoot","MMZidooSync");Dir("ProgramMenuFolder","TARGETDIR",".");Dir("AppMenu","ProgramMenuFolder","MMZidooSync|MM Zidoo Sync");Dir("DesktopFolder","TARGETDIR",".");
Row("Feature","Feature,Title,Level,Attributes","Main","MM Zidoo Sync",1,0);
var dirs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){[""]="INSTALLFOLDER"};int next=0;
string FindDir(string relative){if(dirs.TryGetValue(relative,out var id))return id;var parent=FindDir(Path.GetDirectoryName(relative)??"");id="D"+(++next).ToString("D5");dirs[relative]=id;Dir(id,parent,"d"+next+"|"+Path.GetFileName(relative));return id;}
string? exe=null;
for(int i=0;i<files.Length;i++){var rel=Path.GetRelativePath(source,files[i]);var dir=FindDir(Path.GetDirectoryName(rel)??"");var f="F"+(i+1).ToString("D5");var c="C"+(i+1).ToString("D5");Row("Component","Component,ComponentId,Directory_,Attributes,KeyPath",c,Id("file:"+rel.ToLowerInvariant()),dir,256,f);Row("FeatureComponents","Feature_,Component_","Main",c);var vi=FileVersionInfo.GetVersionInfo(files[i]);var fv=vi.FileVersion;if(!Version.TryParse(fv,out _))fv=null;Row("File","File,Component_,FileName,FileSize,Version,Attributes,Sequence",f,c,"f"+(i+1)+"|"+Path.GetFileName(files[i]),checked((int)new FileInfo(files[i]).Length),fv,16384,i+1);if(rel.Equals("MMZidooSync.exe",StringComparison.OrdinalIgnoreCase))exe=f;}
if(exe is null)throw new InvalidDataException("Missing executable.");
Row("Component","Component,ComponentId,Directory_,Attributes,Condition,KeyPath","Shortcuts",Id("shortcuts"),"INSTALLFOLDER",260,"NOT NOSHORTCUTS","ShortcutMarker");Row("FeatureComponents","Feature_,Component_","Main","Shortcuts");Row("Registry","Registry,Root,Key,Name,Value,Component_","ShortcutMarker",1,@"Software\MMZidooSync\MSI","Shortcuts",app,"Shortcuts");
foreach(var s in new[]{("StartShortcut","AppMenu","MMZidoo|MM Zidoo Sync"),("DesktopShortcut","DesktopFolder","MMZidoo|MM Zidoo Sync (MSI)")})Row("Shortcut","Shortcut,Directory_,Name,Component_,Target,Description,ShowCmd,WkDir",s.Item1,s.Item2,s.Item3,"Shortcuts","[#"+exe+"]","MediaMonkey to Zidoo playlist synchronization",1,"INSTALLFOLDER");
Row("Media","DiskId,LastSequence,Cabinet",1,files.Length,"#payload.cab");Row("Upgrade","UpgradeCode,VersionMin,VersionMax,Attributes,ActionProperty",upgrade,"0.0.0",version.ToString(),257,"OLDPRODUCTS");Row("Upgrade","UpgradeCode,VersionMin,Attributes,ActionProperty",upgrade,version.ToString(),258,"NEWERFOUND");Row("LaunchCondition","Condition,Description","NOT NEWERFOUND OR Installed","A newer or equal installer revision is installed. Use its installer to repair or uninstall.");Row("LaunchCondition","Condition,Description","VersionNT64","64-bit Windows is required.");
var actions=new(string,int,string?)[]{("FindRelatedProducts",25,null),("LaunchConditions",100,null),("CostInitialize",800,null),("FileCost",900,null),("CostFinalize",1000,null),("MigrateFeatureStates",1200,null),("InstallValidate",1400,null),("InstallInitialize",1500,null),("ProcessComponents",1600,null),("UnpublishFeatures",1800,null),("RemoveShortcuts",3200,null),("RemoveFiles",3500,null),("InstallFiles",4000,null),("CreateShortcuts",4500,null),("WriteRegistryValues",5000,null),("RemoveRegistryValues",5100,null),("RegisterUser",6000,null),("RegisterProduct",6100,null),("PublishFeatures",6300,null),("PublishProduct",6400,null),("InstallExecute",6500,null),("RemoveExistingProducts",6550,"OLDPRODUCTS"),("InstallFinalize",6600,null)};
foreach(var a in actions)Row("InstallExecuteSequence","Action,Condition,Sequence",a.Item1,a.Item3,a.Item2);foreach(var a in actions.Where(a=>a.Item2<1400))Row("InstallUISequence","Action,Condition,Sequence",a.Item1,a.Item3,a.Item2);Row("InstallUISequence","Action,Sequence","ExecuteAction",1300);
dynamic stream=db.OpenView("INSERT INTO `_Streams` (`Name`, `Data`) VALUES (?, ?)");dynamic streamRecord=engine.CreateRecord(2);streamRecord.StringData[1]="payload.cab";streamRecord.SetStream(2,Path.Combine(work,"payload.cab"));stream.Execute(streamRecord);stream.Close();Marshal.FinalReleaseComObject(streamRecord);Marshal.FinalReleaseComObject(stream);
dynamic summary=db.SummaryInformation(12);summary.Property[2]="Installation Database";summary.Property[3]="MM Zidoo Sync "+app;summary.Property[4]="Michael Grimsley";summary.Property[7]="x64;1033";summary.Property[9]=Guid.NewGuid().ToString("B").ToUpperInvariant();summary.Property[14]=500;summary.Property[15]=10;summary.Property[19]=2;summary.Persist();db.Commit();Marshal.FinalReleaseComObject(summary);Marshal.FinalReleaseComObject(db);Marshal.FinalReleaseComObject(engine);
Console.WriteLine($"Built {output}; app={app}; MSI={version}; product={product}; files={files.Length}");



