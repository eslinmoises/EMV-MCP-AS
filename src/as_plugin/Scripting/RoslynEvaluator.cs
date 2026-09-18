using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using EMV.AdvanceSteel.Plugin.Commands;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace EMV.AdvanceSteel.Plugin.Scripting
{
    /// <summary>
    /// The context SPEC-003 §3 injects into every script. Script authors address these by name:
    /// <c>Document</c>, <c>Database</c>, <c>ActiveUCS</c>, <c>Output</c>.
    /// </summary>
    public class ScriptGlobals
    {
        public ScriptGlobals(Document document, Database database, Matrix3d activeUcs, StringBuilder output)
        {
            Document = document;
            Database = database;
            ActiveUCS = activeUcs;
            Output = output;
        }

        public Document Document { get; }
        public Database Database { get; }
        public Matrix3d ActiveUCS { get; }
        public StringBuilder Output { get; }

        /// <summary>Convenience for scripts: <c>Print("...")</c> instead of <c>Output.AppendLine("...")</c>.</summary>
        public void Print(object? value) => Output.AppendLine(value?.ToString() ?? "null");
    }

    /// <summary>
    /// POST /api/v1/script/execute — SPEC-003.
    ///
    /// The caller (CommandDispatcher) has already taken the DocumentLock and opened the AutoCAD and
    /// Advance Steel transactions on the main thread, and aborts both when this returns a failure.
    /// That is the rollback guarantee of SPEC-003 §5.3: an uncaught script exception never leaves
    /// half-built geometry behind. A script that needs the open AutoCAD transaction reaches it
    /// through <c>Document.TransactionManager.TopTransaction</c>; SPEC-003 §3 fixes the injected
    /// globals at Document / Database / ActiveUCS / Output, so it is not injected as a fifth.
    /// </summary>
    public static class RoslynEvaluator
    {
        /// <summary>SPEC-003 §5.4.</summary>
        private const int ExecutionTimeoutMs = 30_000;

        private static ScriptOptions? _cachedOptions;

        public static CommandResult Evaluate(Document doc, string scriptCode)
        {
            var output = new StringBuilder();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var globals = new ScriptGlobals(doc, doc.Database, CurrentUcs(doc), output);
                var options = GetScriptOptions();

                var script = CSharpScript.Create(scriptCode, options, typeof(ScriptGlobals));

                var diagnostics = script.Compile();
                var errors = diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString())
                    .ToArray();

                if (errors.Length > 0)
                {
                    stopwatch.Stop();
                    return CommandResult.Fail(
                        "SCRIPT_COMPILATION_FAILED",
                        $"The script failed to compile with {errors.Length} error(s).",
                        400,
                        "Fix the reported diagnostics. Pre-imported namespaces are listed in "
                        + "docs/specs/003-scripting-engine-roslyn.md §4.",
                        string.Join(Environment.NewLine, errors));
                }

                var completed = RunWithTimeout(script, globals, out var returnValue, out var scriptException);

                stopwatch.Stop();

                if (!completed)
                {
                    return CommandResult.Fail(
                        "SCRIPT_TIMEOUT",
                        $"Script execution exceeded the {ExecutionTimeoutMs} ms limit.",
                        504,
                        "Split the work into smaller scripts or avoid unbounded loops. "
                        + "The transaction has been aborted.",
                        Truncate(output.ToString()));
                }

                if (scriptException != null)
                {
                    // Surfacing the failure makes the dispatcher abort both transactions.
                    return CommandResult.Fail(
                        "SCRIPT_EXCEPTION",
                        scriptException.Message,
                        500,
                        "The transaction was aborted and the model is unchanged.",
                        BuildExceptionDetails(scriptException, output));
                }

                return CommandResult.Ok(new
                {
                    success = true,
                    output = Truncate(output.ToString()),
                    return_value = FormatReturnValue(returnValue),
                    execution_time_ms = stopwatch.ElapsedMilliseconds
                });
            }
            catch (CompilationErrorException ex)
            {
                stopwatch.Stop();
                return CommandResult.Fail(
                    "SCRIPT_COMPILATION_FAILED", ex.Message, 400,
                    "Fix the reported diagnostics before resending the script.",
                    string.Join(Environment.NewLine, ex.Diagnostics.Select(d => d.ToString())));
            }
            catch (System.Exception ex)
            {
                stopwatch.Stop();
                return CommandResult.Fail(
                    "SCRIPT_ENGINE_FAILED", ex.Message, 500,
                    "The Roslyn host itself failed; the transaction was aborted.",
                    ex.ToString());
            }
        }

        // ------------------------------------------------------------------
        // Execution
        // ------------------------------------------------------------------

        /// <summary>
        /// Runs the script on the calling thread — it touches the AutoCAD database, which is
        /// single-threaded (rules/transaction-safety.md §1), so it cannot be moved elsewhere and
        /// cannot be killed from outside without risking the drawing.
        ///
        /// The 30 s limit of SPEC-003 §5.4 is therefore enforced two ways: a cancellation token
        /// that Roslyn honours at compilation and at every await point, and an elapsed-time check
        /// that catches a script which blew the budget inside uncancellable native calls. Either
        /// way the caller reports SCRIPT_TIMEOUT and the transaction is rolled back — an overrun
        /// script never commits.
        /// </summary>
        private static bool RunWithTimeout(
            Script<object> script, ScriptGlobals globals, out object? returnValue, out System.Exception? exception)
        {
            returnValue = null;
            exception = null;

            using var cts = new CancellationTokenSource(ExecutionTimeoutMs);
            var elapsed = Stopwatch.StartNew();

            try
            {
                var state = script.RunAsync(globals, cts.Token).GetAwaiter().GetResult();
                returnValue = state.ReturnValue;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return false;
            }
            catch (System.Exception ex)
            {
                exception = Unwrap(ex);
                return true;
            }
            finally
            {
                elapsed.Stop();
            }

            return elapsed.ElapsedMilliseconds <= ExecutionTimeoutMs;
        }

        private static System.Exception Unwrap(System.Exception ex) =>
            ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
                ? aggregate.InnerExceptions[0]
                : ex;

        private static Matrix3d CurrentUcs(Document doc)
        {
            try
            {
                return doc.Editor.CurrentUserCoordinateSystem;
            }
            catch (System.Exception)
            {
                return Matrix3d.Identity;
            }
        }

        // ------------------------------------------------------------------
        // Script options
        // ------------------------------------------------------------------

        /// <summary>
        /// References and imports per SPEC-003 §4. Both lists are filtered against what the running
        /// Advance Steel release actually provides: a namespace that does not exist in this version
        /// would otherwise fail every script with a spurious compile error.
        /// </summary>
        private static ScriptOptions GetScriptOptions()
        {
            if (_cachedOptions != null) return _cachedOptions;

            var assemblies = new List<Assembly>();
            var namespaces = new List<string>
            {
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "System.Text"
            };

            AddAssembly(assemblies, typeof(object).Assembly);                 // System.Private.CoreLib
            AddAssembly(assemblies, typeof(Enumerable).Assembly);             // System.Linq
            AddAssembly(assemblies, typeof(ScriptGlobals).Assembly);          // this plug-in

            // AutoCAD
            AddNamespace(assemblies, namespaces, typeof(Document), "Autodesk.AutoCAD.ApplicationServices");
            AddNamespace(assemblies, namespaces, typeof(Database), "Autodesk.AutoCAD.DatabaseServices");
            AddNamespace(assemblies, namespaces, typeof(Matrix3d), "Autodesk.AutoCAD.Geometry");
            AddNamespace(assemblies, namespaces, typeof(Autodesk.AutoCAD.EditorInput.Editor),
                "Autodesk.AutoCAD.EditorInput");

            // Advance Steel
            AddNamespace(assemblies, namespaces,
                typeof(Autodesk.AdvanceSteel.CADAccess.FilerObject), "Autodesk.AdvanceSteel.CADAccess");
            AddNamespace(assemblies, namespaces,
                typeof(Autodesk.AdvanceSteel.CADLink.Database.ObjectId), "Autodesk.AdvanceSteel.CADLink.Database");
            AddNamespace(assemblies, namespaces,
                typeof(Autodesk.AdvanceSteel.Modelling.StraightBeam), "Autodesk.AdvanceSteel.Modelling");
            AddNamespace(assemblies, namespaces,
                typeof(Autodesk.AdvanceSteel.Geometry.Point3d), "Autodesk.AdvanceSteel.Geometry");
            AddNamespace(assemblies, namespaces,
                typeof(Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement),
                "Autodesk.AdvanceSteel.ConstructionTypes");
            AddNamespace(assemblies, namespaces,
                typeof(Autodesk.AdvanceSteel.Connection.Connection), "Autodesk.AdvanceSteel.Connection");
            AddNamespace(assemblies, namespaces,
                typeof(Autodesk.AdvanceSteel.Profiles.ProfilesManager), "Autodesk.AdvanceSteel.Profiles");

            _cachedOptions = ScriptOptions.Default
                .WithReferences(assemblies)
                .WithImports(namespaces)
                .WithEmitDebugInformation(false);

            return _cachedOptions;
        }

        private static void AddAssembly(ICollection<Assembly> assemblies, Assembly assembly)
        {
            if (!assemblies.Contains(assembly)) assemblies.Add(assembly);
        }

        /// <summary>
        /// Adds the namespace and its declaring assembly, but only if the type resolves in this
        /// Advance Steel installation.
        /// </summary>
        private static void AddNamespace(
            ICollection<Assembly> assemblies, ICollection<string> namespaces, Type probe, string ns)
        {
            try
            {
                AddAssembly(assemblies, probe.Assembly);
                if (!namespaces.Contains(ns)) namespaces.Add(ns);
            }
            catch (System.Exception)
            {
                // Namespace unavailable in this release: skip it rather than break every script.
            }
        }

        // ------------------------------------------------------------------
        // Formatting
        // ------------------------------------------------------------------

        private const int MaxOutputChars = 200_000;

        private static string Truncate(string text) =>
            text.Length <= MaxOutputChars
                ? text
                : text.Substring(0, MaxOutputChars) + $"{Environment.NewLine}... [truncated at {MaxOutputChars} characters]";

        private static string? FormatReturnValue(object? value)
        {
            if (value == null) return null;

            try
            {
                return value.ToString();
            }
            catch (System.Exception)
            {
                return value.GetType().FullName;
            }
        }

        private static string BuildExceptionDetails(System.Exception exception, StringBuilder output)
        {
            var details = new StringBuilder();
            details.AppendLine(exception.ToString());

            if (output.Length > 0)
            {
                details.AppendLine();
                details.AppendLine("--- Script output before the failure ---");
                details.Append(Truncate(output.ToString()));
            }

            return details.ToString();
        }
    }
}
