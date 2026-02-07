
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json;

const string GENERATED_FOLDER = "generated";
const string FUNCTIONS_STORE = "functions.json";

Console.WriteLine("Compile at runtime and debug");
Console.WriteLine("============================");


while (true)
{
    var FUNCTION_REPOSITORY = JsonConvert.DeserializeObject<IList<FnRecord>>(File.ReadAllText(FUNCTIONS_STORE));

    FnRecord? functionItem = null;
    while (functionItem == null)
    {

        Console.WriteLine("Available functions: ");
        foreach (var item in FUNCTION_REPOSITORY!)
            Console.WriteLine(" - " + item.Name);

        Console.WriteLine("Enter the name of the function to execute: ");
        var functionNameToExecute = Console.ReadLine();
        functionNameToExecute ??= "";
        functionNameToExecute = functionNameToExecute.Trim();

        FUNCTION_REPOSITORY = JsonConvert.DeserializeObject<IList<FnRecord>>(File.ReadAllText(FUNCTIONS_STORE));

        functionItem = FUNCTION_REPOSITORY
            .SingleOrDefault(
                x => x.Name.Equals(functionNameToExecute, StringComparison.OrdinalIgnoreCase)
            );

        if (functionItem != null)
            break;
    }
    Console.WriteLine();

    Directory.CreateDirectory(GENERATED_FOLDER);

    var fnAssemblyPath = $"{GENERATED_FOLDER}/{functionItem.Name}.dll";
    var fnAssemblyPdbPath = $"{GENERATED_FOLDER}/{functionItem.Name}.pdb";
    var sourceFilePath = $"{GENERATED_FOLDER}/{functionItem.Name}.cs";

    if (!File.Exists(fnAssemblyPath))
    {
        File.WriteAllLines(sourceFilePath, functionItem.Source!);
        var success = CreateAssembly(fnAssemblyPath, sourceFilePath);
        if (!success)
        {
            Console.WriteLine("Failed to create assembly. Check the errors above.");
            Console.ReadKey();
            continue;
        }
    }

    var classFullQualifiedName = $"CustomCode.{functionItem.Name}";

    Type functionClassType = GetAssembly(fnAssemblyPath, fnAssemblyPdbPath)
            .GetType(classFullQualifiedName)
                    ?? throw new ArgumentException("Class does not exist:" + classFullQualifiedName);

    var field = functionClassType
        .GetField("VERSION", BindingFlags.Public | BindingFlags.Static)
            ?? throw new ArgumentException("Static member VERSION does not exist in class:" + classFullQualifiedName);

    var cachedVersion = (string)(field.GetValue(null) ?? "");

    if (cachedVersion != functionItem.Version)
    {
        foreach (var file in Directory.EnumerateFiles("./", $"{GENERATED_FOLDER}/{functionItem.Name}.*"))
        {
            File.Delete(file);
        }

        File.WriteAllLines(sourceFilePath, functionItem.Source!);

        Console.WriteLine("Database version is different from the compiled version. Generating new version.");
        CreateAssembly(fnAssemblyPath, sourceFilePath);
        functionClassType = GetAssembly(fnAssemblyPath, fnAssemblyPdbPath)
            .GetType(classFullQualifiedName)
        ?? throw new ArgumentException("Class does not exist:" + classFullQualifiedName);
    }

    var executeMethod = functionClassType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                ?? throw new ArgumentException("Method Run does not exist in class:" + classFullQualifiedName);
    string result = (string)(executeMethod.Invoke(null, new object[] { DateTime.Now.ToString() }) ?? "");

    Console.WriteLine("Function returned value: " + result);

    Console.WriteLine();
}

Assembly GetAssembly(string fnAssemblyPath, string fnAssemblyPdbPath)
{
    return Assembly.Load(File.ReadAllBytes(fnAssemblyPath), File.ReadAllBytes(fnAssemblyPdbPath));
}

bool CreateAssembly(string outputFilePath, string sourceCodeFile)
{
    Console.WriteLine("Creating new version...");

    var assemblyName = Path.GetFileName(outputFilePath);
    var code = File.ReadAllText(sourceCodeFile);
    var dotNetCoreDir = Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly!.Location!)!;

    SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
        code,
        encoding: System.Text.Encoding.UTF8,
        path: sourceCodeFile);

    CSharpCompilation compilation = CSharpCompilation.Create(
        assemblyName,
        new[] { syntaxTree },
        new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DynamicAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(dotNetCoreDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(dotNetCoreDir, "System.Linq.dll"))
        },
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithOptimizationLevel(OptimizationLevel.Debug)
            .WithPlatform(Platform.AnyCpu)
    );

    using var peStream = new MemoryStream();
    using var pdbStream = new MemoryStream();

    var emitOptions = new EmitOptions(
        debugInformationFormat: DebugInformationFormat.PortablePdb,

        pdbFilePath: Path.ChangeExtension(assemblyName, "pdb")
    );

    var encoding = System.Text.Encoding.UTF8;

    var buffer = encoding.GetBytes(code);
    var sourceText = SourceText.From(buffer, buffer.Length, encoding, canBeEmbedded: true);

    var embeddedTexts = new List<EmbeddedText>
        {
            EmbeddedText.FromSource(sourceCodeFile, sourceText),
        };

    EmitResult result = compilation.Emit(
        peStream: peStream,
        pdbStream: pdbStream,
            embeddedTexts: embeddedTexts,
        options: emitOptions
    );

    if (result.Success)
    {
        peStream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(outputFilePath, peStream.ToArray());
        pdbStream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(Path.ChangeExtension(outputFilePath, "pdb"), pdbStream.ToArray());
        return true;
    }

    if (result.Diagnostics.Count() > 0)
    {
        var currentColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Compilation failed with the following diagnostics:");
        foreach (var item in result.Diagnostics)
        {
            Console.WriteLine(item.ToString());
        }
        Console.ForegroundColor = currentColor;
    }

    Console.WriteLine("Compilation error. Check the errors above.");
    return false;
}
