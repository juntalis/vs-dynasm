using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using VSDynASM.Properties;

namespace VSDynASM
{
	static class Program
	{
		#region Error Codes

		/// <summary>
		/// The system cannot find the file specified.
		/// </summary>
		const int ERROR_FILE_NOT_FOUND = 0x2;

		#endregion

		#region Private Fields

		private static bool? g_hasDebugEnv = null;
		private static string g_dynAsmFlags = null;
		private static string g_programFolder = null;
		private static readonly Regex  g_wsRegex = new Regex(@"\s");
		private static readonly object g_outputLock = new object();
		
		#endregion

		#region Properties

		/// <summary>
		/// If this environment variable is set, we'll write debug output.
		/// </summary>
		public static string DebugEnvVar
		{
			get { return Settings.Default.DebugEnvVar; }
		}

		/// <summary>
		/// If this environment variable is set, we'll write debug output.
		/// </summary>
		public static string FlagsEnvVar
		{
			get { return Settings.Default.FlagsEnvVar; }
		}

		/// <summary>
		/// Checks <see cref="DebugEnvVar"/> in the current environment variables.
		/// </summary>
		public static bool HasDebugEnv
		{
			get
			{
#if DEBUG
				return true;
#else
				if(!g_hasDebugEnv.HasValue) {
					g_hasDebugEnv = Environment.GetEnvironmentVariable(DebugEnvVar) != null;
				}
				return g_hasDebugEnv.Value;
#endif
			}
		}

		/// <summary>
		/// Checks <see cref="DebugEnvVar"/> in the current environment variables.
		/// </summary>
		public static string DynASMFlags
		{
			get
			{
				return g_dynAsmFlags ?? (
					g_dynAsmFlags = (Environment.GetEnvironmentVariable(FlagsEnvVar) ?? String.Empty).Trim()
				);
			}
		}

		/// <summary>
		/// Absolute path to the folder containing our current executable.
		/// </summary>
		public static string ProgramFolder
		{
			get
			{
				return g_programFolder ?? (
					g_programFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
				);
			}
		}

		#endregion

		/// <summary>
		/// This program is a small tool to workaround anticipated issues that do not actually
		/// exist. The original intent was to write a frontend to dynasm.lua capable of processing
		/// Visual Studio/MSBuild response files. Since we have control over the command-line for
		/// BuildCustomizations, however, this turned out to be unnecessary.
		/// TODO: Phase this out.
		/// </summary>
		/// <param name="args"></param>
		static int Main(string[] args)
		{
			StringBuilder cmdLineBuilder = new StringBuilder();
			if(!String.IsNullOrEmpty(DynASMFlags)) {
				AddArg(cmdLineBuilder, DynASMFlags);
			}

			foreach(string arg in args) {
				if(String.IsNullOrWhiteSpace(arg)) continue;
				if(arg.StartsWith("@") && arg.Length > 1) {
					foreach(string rspArg in ProcessResponseFile(arg.Substring(1)))
						AddArg(cmdLineBuilder, rspArg);
				} else {
					AddArg(cmdLineBuilder, ProcessArg(arg));
				}
			}

			// Resolve all parts of the command line args.
			string luaExecutable = GetLuaExecutablePath();
			string dynasmScript = GetDynASMScriptPath();
			string dynAsmArgs = cmdLineBuilder.ToString();
			string toolName = Path.GetFileNameWithoutExtension(dynasmScript);

			// Trace execution
			DebugLine(@"Invoking dynasm with the following parameters:");
			DebugLine(@"    Lua Executable => '{0}'", luaExecutable);
			DebugLine(@"    DynASM Script  => '{0}'", dynasmScript);
			DebugLine(@"    DynASM Args    => '{0}'", dynAsmArgs);

			// Put all the cmdline parts together into a single string. dynAsmArgs will
			// already start with a space, so there's no need to add a new one.
			string dynAsmCmdLine = dynasmScript + dynAsmArgs;

			ProcessStartInfo psi = new ProcessStartInfo(luaExecutable) {
				CreateNoWindow = true,
				UseShellExecute = false,
				Arguments = dynAsmCmdLine,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				RedirectStandardInput = true,
				FileName = luaExecutable
			};

			// We don't do anything with the StandardInput redirect, but it exists to workaround bugs. See:
			// http://bugs.python.org/issue3905
			// and https://github.com/kripken/emscripten/issues/718
			int processReturnCode = 1;
			try {
				Process p = Process.Start(psi);
				if(p == null) throw new InvalidOperationException(String.Format("Failed to start {0}", luaExecutable));
				p.OutputDataReceived += new DataReceivedEventHandler(p_OutputDataReceived);
				p.ErrorDataReceived += new DataReceivedEventHandler(p_ErrorDataReceived);
				p.BeginOutputReadLine();
				p.BeginErrorReadLine();

				while (!p.HasExited) {
					p.WaitForExit(10000);
					if (!p.HasExited) DebugLine("{0} running.. please wait.", toolName);
				}
				processReturnCode = p.ExitCode;
			} catch (Exception e) {
				Console.WriteLine("Exception received when starting tool '" + psi.FileName + "' with command line '" + psi.Arguments + "'!\n" + e);
				return 1;
			}

			if(HasDebugEnv) Thread.Sleep(500);
			DebugLine(@"Process completed with exit code: {0}", processReturnCode);
			return processReturnCode;
		}

		static void p_OutputDataReceived(object sender, DataReceivedEventArgs line)
		{
			if(line.Data != null) {
				if (line.Data.EndsWith("\n"))
					Console.Out.Write(line.Data);
				else
					Console.Out.WriteLine(line.Data);
				Console.Out.Flush();
			}
		}

		static void p_ErrorDataReceived(object sender, DataReceivedEventArgs line)
		{
			if(line.Data != null) {
				if (line.Data.EndsWith("\n"))
					Console.Error.Write(line.Data);
				else
					Console.Error.WriteLine(line.Data);
				Console.Error.Flush();
			}
		}

		#region Command Line Argument Handling

		/// <summary>
		/// 
		/// </summary>
		/// <param name="cmdLineBuilder">Command line args builder</param>
		/// <param name="arg">Command line argument</param>
		static void AddArg(StringBuilder cmdLineBuilder, string arg)
		{
			cmdLineBuilder.Append(" " + arg);
		}

		/// <summary>
		/// Quotes args as necessary.
		/// </summary>
		/// <param name="arg"></param>
		/// <returns></returns>
		static string QuoteIfNeeded(string arg)
		{
			return g_wsRegex.IsMatch(arg) && !arg.StartsWith("\"") ?
			       String.Format("\"{0}\"", arg) : arg;
		}

		/// <summary>
		/// Quotes any arguments that require it and replaces any / prefixes.
		/// </summary>
		/// <param name="arg">Command line argument</param>
		/// <returns>Processed arg</returns>
		static string ProcessArg(string arg)
		{
			string fixedArg = arg;
			if(arg.StartsWith("/")) {
				fixedArg = String.Format("{0}{1}", arg.Length > 2 ? "--" : "-", arg.Substring(1));
			}
			return QuoteIfNeeded(fixedArg);
		}

		/// <summary>
		/// Processes a response file.
		/// </summary>
		/// <param name="rspFile">Response file path</param>
		/// <returns></returns>
		static IEnumerable<String> ProcessResponseFile(string rspFile)
		{
			if(!File.Exists(rspFile)) {
				FatalError(ERROR_FILE_NOT_FOUND, @"Response file, ""{0}"" does not exist.", rspFile);
			}

			// Iterate through file lines, clean them up, and yield them.
			DebugLine(@"Processing response file: {0}", rspFile);
			using(StreamReader oReader = File.OpenText(rspFile)) {
				while(oReader.Peek() >= 0) {
					string rawLine = oReader.ReadLine();
					if(!String.IsNullOrWhiteSpace(rawLine)) {
						string cleanLine = rawLine.Trim('\r', ' ', '\t');
						yield return cleanLine;
					}
				}
			}
		}

		#endregion

		#region DynASM Paths Resolution

		static string FindLuaExecutableOnPath(string luaExeName)
		{
			// Add an .exe if no extensions exists.
			if(luaExeName.Equals(Path.GetFileNameWithoutExtension(luaExeName), StringComparison.OrdinalIgnoreCase))
				luaExeName = String.Format("{0}.exe", luaExeName);

			// ReSharper disable once PossibleNullReferenceException
			string[] envPaths = Environment.GetEnvironmentVariable("PATH").Split(';');

			// Search the PATH environment variable for the lua executable name.
			foreach(string envPath in envPaths) {
				string pathToCheck = Path.Combine(envPath, luaExeName);
				if(File.Exists(pathToCheck))
					return pathToCheck;
			}

			return null;
		}

		/// <summary>
		/// Get the absolute path to the lua executable we'll be using to run <c>dynasm.lua</c>. This
		/// is resolved by checking the value of our "LuaExecutable" setting. If that does not contain
		/// the absolute path to an existing file, we then add the directory of "vsdynasm.exe" and try
		/// again. If that still does not point to an existing file, we search the PATH environment variable
		/// for a filename matching our "LuaExecutable" setting. If none is found, we error out.
		/// </summary>
		/// <returns>Absolute path to lua executable</returns>
		static string GetLuaExecutablePath()
		{
			string luaExeName = Settings.Default.LuaExecutable;
			string luaExePath = luaExeName;
			// Seems sloppy to nest the same condition over and over like this, but it was the simplest
			// way without rechecking a passed condition.
			if(!File.Exists(luaExePath)) {
				luaExePath = Path.Combine(ProgramFolder, luaExeName);
				if(!File.Exists(luaExePath)) {
					luaExePath = FindLuaExecutableOnPath(luaExeName);
					if(String.IsNullOrEmpty(luaExePath)) {
						FatalError(ERROR_FILE_NOT_FOUND, @"Could not locate lua executable at path, ""{0}"".", luaExeName);
					}
				}
			}
			return QuoteIfNeeded(luaExePath);
		}

		/// <summary>
		/// Resolves the path to <c>dynasm.lua</c>. Attempts are made in the following order:
		/// 
		/// {DynAsmDir}\{DynAsmScript}
		/// bin\..\{DynAsmDir}\{DynAsmScript}
		/// 
		/// </summary>
		/// <returns>Absolute path to <c>dynasm.lua</c></returns>
		static string GetDynASMScriptPath()
		{
			string dynAsmPath = null,
			       dynAsmDir = Settings.Default.DynAsmDir,
			       dynAsmScript = Settings.Default.DynAsmScript;
			if(Directory.Exists(dynAsmDir)) {
				dynAsmPath = Path.Combine(dynAsmDir, dynAsmScript);
			} else {
				string prefixDir = Path.GetDirectoryName(ProgramFolder);
				// ReSharper disable once AssignNullToNotNullAttribute
				dynAsmPath = Path.Combine(prefixDir, dynAsmDir, dynAsmScript);
				
			}
			if(!File.Exists(dynAsmPath)) {
				FatalError(ERROR_FILE_NOT_FOUND, @"Could not locate dynasm script at path, ""{0}"".", dynAsmPath);
			}
			return QuoteIfNeeded(dynAsmPath);
		}

		#endregion

		#region Misc Utility Methods

		/// <summary>
		/// Sets the console color, writes a timestamped prefix to specified <paramref name="consoleWriter"/>,
		/// and reverts the color back to the original.
		/// </summary>
		/// <param name="consoleWriter"></param>
		/// <param name="prefixColor"></param>
		/// <param name="prefix"></param>
		static void WritePrefix(TextWriter consoleWriter, ConsoleColor prefixColor, string prefix)
		{
			consoleWriter.Flush();
			string timeStamp = String.Format(@"[{0:MM-dd HH-yyyy:mm:ss}]", DateTime.Now);
			try {
				ConsoleColor oldColor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.DarkGray;
				consoleWriter.Write(timeStamp);
				Console.ForegroundColor = prefixColor;
				consoleWriter.Write(@" {0}", prefix);
				Console.ForegroundColor = oldColor;
				consoleWriter.Write(@" - ");
			} catch {
				consoleWriter.Write(@"{0} {1} - ", timeStamp, prefix);
			}
			consoleWriter.Flush();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="message"></param>
		/// <param name="args"></param>
		static void DebugLine(string message, params object[] args)
		{
			if(!HasDebugEnv) return;
			WritePrefix(Console.Out, ConsoleColor.Green, @"DEBUG");
			Console.Out.WriteLine(message, args);
			Console.Out.Flush();
		}

		/// <summary>
		/// Exits the program with specified <paramref name="exitCode"/> and an
		/// error message built from the format string, <paramref name="message"/> and
		/// the arguments <paramref name="args"/>
		/// </summary>
		/// <param name="exitCode"></param>
		/// <param name="message"></param>
		/// <param name="args"></param>
		static void FatalError(int exitCode, string message, params object[] args)
		{
			WritePrefix(Console.Error, ConsoleColor.Red, @"ERROR");
			Console.Error.WriteLine(message, args);
			Console.Error.Flush();
			Environment.Exit(exitCode);
		}

		/// <summary>
		/// Exits the program with an exit code of 1. Displays an error message built 
		/// from the format string, <paramref name="message"/> and the arguments 
		/// <paramref name="args"/>
		/// </summary>
		/// <param name="message"></param>
		/// <param name="args"></param>
		static void FatalError(string message, params object[] args)
		{
			FatalError(1, message, args);
		}

		#endregion
	}
}
