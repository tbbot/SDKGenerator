using System;
using System.Collections.Generic;

public interface ICommand
{
    string[] CommandKeys { get; }
    string[] MandatoryArgKeys { get; }
    int Execute(Dictionary<string, string> argsLc, Dictionary<string, string> argsCased);
}

namespace JenkinsConsoleUtility
{
    public static class JenkinsConsoleUtility
    {
        public static int Main(string[] args)
        {
            var commandLookup = FindICommands();
            List<string> orderedCommands;
            Dictionary<string, string> lcArgsByName, casedArgsByName;
            ICommand tempCommand;

            try
            {
                ExtractArgs(args, out orderedCommands, out lcArgsByName, out casedArgsByName);

                var success = true;
                foreach (var key in orderedCommands)
                {
                    if (!commandLookup.TryGetValue(key, out tempCommand))
                    {
                        success = false;
                        FancyWriteToConsole("Unexpected command: " + key, null, ConsoleColor.Red);
                    }
                }

                if (orderedCommands.Count == 0)
                    FancyWriteToConsole("No commands given, no work will be done", null, ConsoleColor.DarkYellow);
                if (!success || orderedCommands.Count == 0)
                    throw new Exception("Commands not input correctly");
            }
            catch (Exception)
            {
                FancyWriteToConsole("Run a sequence of ordered commands --<command> [--<command> ...] [-<argKey> <argValue> ...]\nValid commands:", commandLookup.Keys, ConsoleColor.Yellow);
                FancyWriteToConsole("argValues can have spaces.  Dashes in argValues can cause problems, and are not recommended.", null, ConsoleColor.Yellow);
                FancyWriteToConsole("Quotes are considered part of the argKey or argValue, and are not parsed as tokens.", null, ConsoleColor.Yellow);
                // TODO: Report list of available commands and valid/required args for commands
                return Pause(1);
            }

            var returnCode = 0;
            foreach (var key in orderedCommands)
            {
                if (returnCode == 0 && commandLookup.TryGetValue(key, out tempCommand))
                {
                    returnCode = VerifyKeys(tempCommand, lcArgsByName);
                    if (returnCode == 0)
                        returnCode = tempCommand.Execute(lcArgsByName, casedArgsByName);
                }
                if (returnCode != 0)
                {
                    FancyWriteToConsole(key + " command returned error code: " + returnCode, null, ConsoleColor.Yellow);
                    return Pause(returnCode);
                }
            }

            return Pause(0);
        }

        /// <summary>
        /// Try to find the given (lowercased) key in the command line arguments, or the environment variables.
        /// If it is present, return it
        /// Else getDefault (if defined), else throw an exception
        /// Defined as empty string is considered a successful result
        /// </summary>
        /// <returns>The string associated with this key</returns>
        public static string GetArgVar(Dictionary<string, string> args, string key, string getDefault = null)
        {
            string output;
            if (TryGetArgVar(out output, args, key, getDefault))
                return output;

            if (getDefault != null) // Don't use string.IsNullOrEmpty() here, because there's a distinction between "undefined" and "empty"
            {
                FancyWriteToConsole("GetArgVar: " + key + " not defined, reverting to: " + getDefault, null, ConsoleColor.DarkYellow);
                return getDefault;
            }

            var msg = "ERROR: Required parameter: " + key + " not found";
            FancyWriteToConsole(msg, null, ConsoleColor.Red);
            throw new Exception(msg);
        }

        /// <summary>
        /// Try to find the given key in the command line arguments, or the environment variables.
        /// If it is present, return it.
        /// Defined as empty string returns true
        /// Falling through to getDefault always returns false
        /// </summary>
        /// <returns>True if the key is found</returns>
        public static bool TryGetArgVar(out string output, Dictionary<string, string> args, string key, string getDefault = null)
        {
            var lcKey = key.ToLower();

            // Check in args (not guaranteed to be lc-keys)
            output = null;
            if (args != null)
                foreach (var eachArgKey in args.Keys)
                    if (eachArgKey.ToLower() == lcKey)
                        output = args[eachArgKey];
            if (output != null) // Don't use string.IsNullOrEmpty() here, because there's a distinction between "undefined" and "empty"
                return true;

            // Check in env-vars - Definitely not lc-keys
            var allEnvVars = Environment.GetEnvironmentVariables();
            foreach (string eachEnvKey in allEnvVars.Keys)
                if (eachEnvKey.ToLower() == lcKey)
                    output = allEnvVars[eachEnvKey] as string;
            if (output != null) // Don't use string.IsNullOrEmpty() here, because there's a distinction between "undefined" and "empty"
                return true;

            output = getDefault;
            return false;
        }

        private static int VerifyKeys(ICommand cmd, Dictionary<string, string> argsByName)
        {
            if (cmd.MandatoryArgKeys == null || cmd.MandatoryArgKeys.Length == 0)
                return 0;

            var allEnvVars = Environment.GetEnvironmentVariables();
            List<string> missingArgKeys = new List<string>();

            foreach (var eachCmdKey in cmd.MandatoryArgKeys)
            {
                var expectedKey = eachCmdKey.ToLower();
                var found = argsByName.ContainsKey(expectedKey);
                if (!found)
                    foreach (string envKey in allEnvVars.Keys)
                        if (envKey.ToLower() == expectedKey)
                            found = true;
                if (!found)
                    missingArgKeys.Add(expectedKey);
            }

            foreach (var eachCmdKey in missingArgKeys)
                FancyWriteToConsole(cmd.CommandKeys[0] + " - Missing argument: " + eachCmdKey, null, ConsoleColor.Yellow);
            return missingArgKeys.Count;
        }

        public static void FancyWriteToConsole(string msg = null, ICollection<string> multiLineMsg = null, ConsoleColor textColor = ConsoleColor.White)
        {
            Console.ForegroundColor = textColor;
            if (!string.IsNullOrEmpty(msg))
                Console.WriteLine(msg);
            if (multiLineMsg != null)
                foreach (var eachMsg in multiLineMsg)
                    Console.WriteLine(eachMsg);
            Console.ForegroundColor = ConsoleColor.White;
        }

        private static int Pause(int code)
        {
            FancyWriteToConsole("Done! Press any key to close", null, code == 0 ? ConsoleColor.Green : ConsoleColor.DarkRed);
            try
            {
                Console.ReadKey();
            }
            catch (InvalidOperationException)
            {
                // ReadKey fails when run from inside of Jenkins, so just ignore it.
            }
            return code;
        }

        private static Dictionary<string, ICommand> FindICommands()
        {
            // Just hard code the list for now
            var commandLookup = new Dictionary<string, ICommand>();

            var iCommands = typeof(ICommand);
            var cmdTypes = new List<Type>();
            foreach (var eachAssembly in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var eachType in eachAssembly.GetTypes())
                    if (iCommands.IsAssignableFrom(eachType) && eachType.Name != iCommands.Name)
                        cmdTypes.Add(eachType);

            foreach (var eachPkgType in cmdTypes)
            {
                var eachInstance = (ICommand)Activator.CreateInstance(eachPkgType);
                foreach (var eachCmdKey in eachInstance.CommandKeys)
                    commandLookup.Add(eachCmdKey.ToLower(), eachInstance);
            }
            return commandLookup;
        }

        /// <summary>
        /// Extract command line arguments into target information
        /// </summary>
        private static void ExtractArgs(string[] args, out List<string> orderedCommands, out Dictionary<string, string> lcArgsByName, out Dictionary<string, string> casedArgsByName)
        {
            orderedCommands = new List<string>();
            lcArgsByName = new Dictionary<string, string>();
            casedArgsByName = new Dictionary<string, string>();

            string activeKeyLc = null;
            string activeKeyCased = null;
            foreach (var eachArgCased in args)
            {
                var eachArgLc = eachArgCased.ToLower();
                if (eachArgLc.StartsWith("--"))
                {
                    activeKeyLc = eachArgLc.Substring(2);
                    activeKeyCased = eachArgCased.Substring(2);
                    orderedCommands.Add(activeKeyLc);
                }
                else if (eachArgLc.StartsWith("-"))
                {
                    activeKeyLc = eachArgLc.Substring(1);
                    activeKeyCased = eachArgCased.Substring(1);
                    lcArgsByName[activeKeyLc] = "";
                    casedArgsByName[activeKeyCased] = "";
                }
                else if (activeKeyCased == null)
                {
                    throw new Exception("Unexpected token: " + eachArgCased);
                }
                else
                {
                    lcArgsByName[activeKeyLc] = (lcArgsByName[activeKeyLc] + " " + eachArgLc).Trim();
                    casedArgsByName[activeKeyCased] = (casedArgsByName[activeKeyCased] + " " + eachArgCased).Trim();
                }
            }
        }
    }
}
