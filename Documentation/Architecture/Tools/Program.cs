using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.Combine(root, "Documentation/Architecture");
var app = Path.Combine(root, "MediaMonkeyZidooSync");
string Run(string exe, params string[] arguments) {
    var p = new ProcessStartInfo(exe) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in arguments) p.ArgumentList.Add(a);
    using var child = Process.Start(p)!;
    var text = child.StandardOutput.ReadToEnd(); var errors = child.StandardError.ReadToEnd(); child.WaitForExit();
    if (child.ExitCode != 0) throw new Exception(errors + text);
    return text;
}
var input = JsonDocument.Parse(Run("dotnet", "msbuild", "MediaMonkeyZidooSync/MediaMonkeyZidooSync.csproj", "-t:ResolveReferences", "-getItem:Compile,ReferencePath", "-nologo"));
var files = input.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray().Select(x => x.GetProperty("FullPath").GetString()!).ToList();
var authored = files.Where(f => !Path.GetRelativePath(app,f).StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
var generated = Path.Combine(app, "obj/Debug/net9.0-windows");
files.AddRange(Directory.GetFiles(generated, "*.cs", SearchOption.AllDirectories).Where(f => !f.EndsWith(".g.i.cs")));
var parse = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: new[]{"DEBUG","TRACE","NET","NET9_0","WINDOWS","NET9_0_OR_GREATER","WINDOWS7_0_OR_GREATER"});
var trees = files.Distinct(StringComparer.OrdinalIgnoreCase).Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f)).ToArray();
var refs = input.RootElement.GetProperty("Items").GetProperty("ReferencePath").EnumerateArray().Select(x => MetadataReference.CreateFromFile(x.GetProperty("FullPath").GetString()!));
var compilation = CSharpCompilation.Create("MMZidooSync", trees, refs, new CSharpCompilationOptions(OutputKind.WindowsApplication, nullableContextOptions: NullableContextOptions.Enable));
var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
if (errors.Length > 0) throw new Exception(string.Join("\n", errors.Take(20).Select(d => d.ToString())));
var types = new SortedDictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
var locations = new Dictionary<string, (string File, int Line)>();
string TypeId(INamedTypeSymbol s) => s.OriginalDefinition.ToDisplayString();
foreach (var tree in trees.Where(t => authored.Contains(t.FilePath))) {
    var model = compilation.GetSemanticModel(tree);
    foreach (var node in tree.GetRoot().DescendantNodes().Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)) {
        if (model.GetDeclaredSymbol(node) is INamedTypeSymbol s) {
            var id = TypeId(s); types[id] = s;
            locations[id] = (Path.GetRelativePath(root, tree.FilePath).Replace('\\','/'), node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
        }
    }
}
string Layer(string id) {
    var file = locations[id].File; var name = Path.GetFileName(file);
    if (file.Contains("/Models/")) return "Models";
    if (name == "ApplicationBranding.cs") return "Branding";
    if (!file.Contains("/Services/") || name is "GridSelectionBehavior.cs" or "WindowsThemeService.cs") return "Presentation";
    return "Services";
}
var typeEdges = new SortedSet<(string From, string To)>();
var calls = new SortedSet<(string From, string To)>();
var methods = new SortedDictionary<string, (string Type, string Label, string File, int Line)>();
string Method(IMethodSymbol s) {
    s = (s.ReducedFrom ?? s).OriginalDefinition;
    var id = s.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    if (!methods.ContainsKey(id)) {
        var loc = s.Locations.FirstOrDefault(l => l.IsInSource);
        methods[id] = (TypeId(s.ContainingType), s.ContainingType.Name + "." + s.Name, loc == null ? "" : Path.GetRelativePath(root,loc.SourceTree!.FilePath).Replace('\\','/'), loc?.GetLineSpan().StartLinePosition.Line + 1 ?? 0);
    }
    return id;
}
foreach (var tree in trees.Where(t => authored.Contains(t.FilePath))) {
    var model = compilation.GetSemanticModel(tree);
    foreach (var node in tree.GetRoot().DescendantNodes()) {
        var owner = model.GetEnclosingSymbol(node.SpanStart);
        var fromType = owner as INamedTypeSymbol ?? owner?.ContainingType;
        if (fromType == null || !types.ContainsKey(TypeId(fromType))) continue;
        if (node is NameSyntax or MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax) {
            var symbol = model.GetSymbolInfo(node).Symbol;
            var toType = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
            if (toType != null && types.ContainsKey(TypeId(toType)) && TypeId(fromType) != TypeId(toType)) typeEdges.Add((TypeId(fromType),TypeId(toType)));
        }
        if (node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax && model.GetSymbolInfo(node).Symbol is IMethodSymbol) {
            var target = model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            while (owner != null && owner is not IMethodSymbol) owner = owner.ContainingSymbol;
            if (owner is IMethodSymbol source && target != null && types.ContainsKey(TypeId(target.ContainingType))) calls.Add((Method(source),Method(target)));
        }
    }
}
XNamespace dg = "http://schemas.microsoft.com/vs/2009/dgml";
XElement Node(string id, string label, string category) => new(dg+"Node",new XAttribute("Id",id),new XAttribute("Label",label),new XAttribute("Category",category));
XElement Link(string a,string b,string category) => new(dg+"Link",new XAttribute("Source",a),new XAttribute("Target",b),new XAttribute("Category",category));
void SaveMap(string name, IEnumerable<XElement> nodes,IEnumerable<XElement> links) {
    var graph = new XElement(dg+"DirectedGraph",new XAttribute("GraphDirection","LeftToRight"),new XAttribute("Layout","Sugiyama"),new XElement(dg+"Nodes",nodes),new XElement(dg+"Links",links),
        new XElement(dg+"Categories",new[]{"Presentation","Services","Models","Branding","Calls","Uses","Contains","ProjectReference","LinkedSource"}.Select(c=>new XElement(dg+"Category",new XAttribute("Id",c),new XAttribute("Label",c)))),
        new XElement(dg+"Styles",new[]{("Presentation","#FFDCEBFA"),("Services","#FFDFF2DE"),("Models","#FFFFEABF"),("Branding","#FFEBDFF5")}.Select(x=>new XElement(dg+"Style",new XAttribute("TargetType","Node"),new XElement(dg+"Condition",new XAttribute("Expression",$"HasCategory('{x.Item1}')")),new XElement(dg+"Setter",new XAttribute("Property","Background"),new XAttribute("Value",x.Item2))))));
    new XDocument(graph).Save(Path.Combine(output,name));
}
XElement TypeNode(string id) { var n=Node(id,types[id].Name,Layer(id)); n.Add(new XAttribute("SourceLocation","../../"+locations[id].File), new XAttribute("SourceLineNumber",locations[id].Line),new XAttribute("Description",id));return n; }
SaveMap("Types.dgml",types.Keys.Select(TypeNode),typeEdges.Select(e=>Link(e.From,e.To,"Uses")));
XElement MethodNode(string id) { var m=methods[id]; var n=Node(id,m.Label,Layer(m.Type));n.Add(new XAttribute("Description",id),new XAttribute("SourceLocation","../../"+m.File),new XAttribute("SourceLineNumber",m.Line));return n; }
SaveMap("Calls.dgml",methods.Keys.Select(MethodNode),calls.Select(e=>Link(e.From,e.To,"Calls")));
var focusNames=new[]{"MainWindow","SyncRunner","NativeProductionSyncService","NativeProductionTransaction","NativeInterruptedRecovery","NativeProductionRecovery","NativeZidooPlaylistDevice","NativeZidooMusicPlaylistService","NativeZidooConnectivityPowerService","MediaMonkeyPlaylistExporter","SynchronizationWorkflow"};
var focused=typeEdges.Where(e=>focusNames.Contains(types[e.From].Name)&&focusNames.Contains(types[e.To].Name)).ToArray();
SaveMap("Synchronization.dgml",types.Keys.Where(id=>focusNames.Contains(types[id].Name)).Select(TypeNode),focused.Select(e=>Link(e.From,e.To,"Uses")));
var focusedCalls=calls.Where(e=>focusNames.Contains(types[methods[e.From].Type].Name)&&focusNames.Contains(types[methods[e.To].Type].Name)&&methods[e.From].Type!=methods[e.To].Type).ToArray();
SaveMap("SynchronizationCalls.dgml",focusedCalls.SelectMany(e=>new[]{e.From,e.To}).Distinct().Order().Select(MethodNode),focusedCalls.Select(e=>Link(e.From,e.To,"Calls")));
var layerNames=types.Keys.Select(Layer).Distinct().Order().ToArray();
var layerEdges=typeEdges.Select(e=>(From:Layer(e.From),To:Layer(e.To))).Where(e=>e.From!=e.To).Distinct().Order().ToArray();
SaveMap("Layers.dgml",layerNames.Select(l=>Node(l,l,l)),layerEdges.Select(e=>Link(e.From,e.To,"Uses")));
// Only tracked projects are mapped. Private archives and retired backups never enter these graphs.
var projectFiles=Run("git","-c","safe.directory="+root.Replace('\\','/'),"ls-files","*.csproj").Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Order().ToArray();
var projectLinks=new List<XElement>(); var projectNodes=new List<XElement>();
foreach(var p in projectFiles) {
    projectNodes.Add(Node(p,Path.GetFileNameWithoutExtension(p),"Project"));var xml=XDocument.Load(Path.Combine(root,p));
    foreach(var r in xml.Descendants().Where(e=>e.Name.LocalName=="ProjectReference" || e.Name.LocalName=="Compile")) {
        var include=(string?)r.Attribute("Include");if(include==null||include.Contains('*')||include.Contains(';'))continue;
        var target=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(p)!,include))).Replace('\\','/');
        if(r.Name.LocalName=="ProjectReference")projectLinks.Add(Link(p,target,"ProjectReference"));
        else if(target.StartsWith("MediaMonkeyZidooSync/")) {if(!projectNodes.Any(n=>(string?)n.Attribute("Id")==target))projectNodes.Add(Node(target,Path.GetFileName(target),"Source"));projectLinks.Add(Link(p,target,"LinkedSource"));}
    }
}
SaveMap("Projects.dgml",projectNodes,projectLinks);
File.WriteAllText(Path.Combine(output,"source-evidence.json"),JsonSerializer.Serialize(new{SourceFiles=authored.Count,Types=types.Count,TypeDependencies=typeEdges.Count,Calls=calls.Count,Layers=layerNames,LayerDependencies=layerEdges.Select(e=>new{e.From,e.To}),Sources=authored.Order().Select(f=>new{Path=Path.GetRelativePath(root,f).Replace('\\','/'),SHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))})},new JsonSerializerOptions{WriteIndented=true}));
// The validation contract is created once. Regeneration never silently broadens allowed dependencies.
var layerFile=Path.Combine(output,"DependencyValidation.layerdiagram");
if(!File.Exists(layerFile)) {
    XNamespace ns="http://schemas.microsoft.com/VisualStudio/TeamArchitect/LayerDesigner";
    Guid Id(string s)=>new(MD5.HashData(Encoding.UTF8.GetBytes(s)));
    var layers=new XElement(ns+"layers");
    foreach(var l in layerNames) {
        var layer=new XElement(ns+"layer",new XAttribute("Id",Id(l)),new XAttribute("name",l));
        var references=new XElement(ns+"references");
        foreach(var id in types.Keys.Where(id=>Layer(id)==l && types[id].ContainingType==null)) {
            var s=types[id];var artifact=$"(Namespace={s.ContainingNamespace.ToDisplayString()} Type={s.MetadataName})";
            references.Add(new XElement(ns+"reference",new XAttribute("Id",Id(id)),new XAttribute("name",s.Name),new XElement(ns+"ArtifactNode",new XAttribute("Id",artifact),new XAttribute("Category","CodeSchema_Class"),new XAttribute("Label",s.Name)),new XElement(ns+"ExpandedNode",new XAttribute("Id",artifact),new XAttribute("Category","CodeSchema_Class"),new XAttribute("Label",s.Name),new XElement(ns+"LinkCategory",new XAttribute("Ref","Represents")))));
        }
        layer.Add(references,new XElement(ns+"dependencyToLayers",layerEdges.Where(e=>e.From==l).Select(e=>new XElement(ns+"dependencyFromLayerToLayer",new XAttribute("Id",Id(e.From+e.To)),new XAttribute("direction","Forward"),new XElement(ns+"layerMoniker",new XAttribute("Id",Id(e.To)))))));
        layers.Add(layer);
    }
    new XDocument(new XElement(ns+"layerModel",new XAttribute("Id",Id("MMZidooSync")),new XAttribute("name","MM Zidoo Sync architecture"),layers)).Save(layerFile);
}
Console.WriteLine($"Mapped {authored.Count} source files, {types.Count} types, {typeEdges.Count} dependencies, {calls.Count} calls.");
Console.WriteLine(string.Join("\n",layerEdges.Select(e=>$"{e.From} -> {e.To}")));
