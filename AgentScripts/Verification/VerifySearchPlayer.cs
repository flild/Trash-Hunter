using System.IO;
using System.Reflection;
using Unity.Pipeline.Scripting;

// Pipeline 1.0.0 does not expose additional assembly references on script eval.
// Run the same route against Input System and VContainer in the development Player.
var checkArgs = Environment.GetCommandLineArgs();
var checkIndex = Array.IndexOf(checkArgs, "-trash-hunter-check");
var check = checkIndex >= 0 && checkIndex + 1 < checkArgs.Length ? checkArgs[checkIndex + 1] : null;
var routeFile = check?.StartsWith("i120-", StringComparison.Ordinal) == true
    ? "AgentScripts/Verification/VerifySearchAcceptance.cs"
    : check?.StartsWith("playtest-", StringComparison.Ordinal) == true
    ? "AgentScripts/Verification/VerifyPlaytest.cs"
    : checkIndex >= 0
    ? "AgentScripts/Verification/VerifyExpedition.cs"
    : typeof(TrashHunter.GameState).GetProperty("Bag") == null
    ? "AgentScripts/Verification/VerifySearchPrototype.cs"
    : "AgentScripts/Verification/VerifyBagSelection.cs";
var source = Path.GetFullPath(routeFile);
var assembly = typeof(Wait).Assembly;
var requestType = assembly.GetType("Unity.Pipeline.Compilation.CompilationRequest", true);
var compiler = assembly.GetType("Unity.Pipeline.Compilation.RoslynCompilationService", true);
var request = Activator.CreateInstance(requestType, nonPublic: true);
requestType.GetProperty("SourceCode").SetValue(request, File.ReadAllText(source));
requestType.GetProperty("AssemblyName").SetValue(request, $"SearchPlayerProbe_{Guid.NewGuid():N}");
requestType.GetProperty("AdditionalAssemblyPrefixes").SetValue(request, new[] { "Unity.InputSystem", "VContainer", "Newtonsoft.Json", "System" });
requestType.GetProperty("ScriptUsings", BindingFlags.Instance | BindingFlags.NonPublic)
    .SetValue(request, new[] { "Unity.Pipeline.Scripting" });
var result = compiler.GetMethod("Compile", new[] { requestType }).Invoke(null, new[] { request });
var resultType = result.GetType();
var entry = (MethodInfo)resultType.GetProperty("ScriptEntryPoint", BindingFlags.Instance | BindingFlags.NonPublic)
    .GetValue(result);
if (entry == null)
{
    var diagnostics = (System.Collections.IEnumerable)resultType.GetProperty("Diagnostics").GetValue(result);
    var messages = diagnostics.Cast<object>().Select(d => d.GetType().GetProperty("Message").GetValue(d));
    throw new InvalidOperationException(string.Join(Environment.NewLine, messages));
}
var route = (Task<object>)entry.Invoke(null, new object[] { new object[2] });
return await route;
