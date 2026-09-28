using ioBroker_NewGen.Controllers;
using RSEV.Utilities.Logging;
using RSEV.Utilities.Runtime;

namespace ioBroker_NewGen.Runtime
{
    public class UnexpectedUserExitHandler : RSEV.Utilities.Runtime.UnexpectedExitHandler
    {
        public UnexpectedUserExitHandler(IRuntimeContext context) : base(context)
        {

        }

        public static async Task RunCommandLoopAsync()
        {
            while (isRunning)
            {
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                var safeInput = input!;
                SystemLog.Log(0, SystemLog.LogColor.Gray, $"Eingabe empfangen (Länge={safeInput.Length})");

                if (RuntimeContext.CommandHandler.IsCommand(safeInput, out var parts))
                {
                    var cmd = parts?.Length > 0 ? parts[0] : "<leer>";
                    try
                    {
                        await RuntimeContext.CommandHandler.ExecuteAsync(safeInput);
                    }
                    catch (Exception ex)
                    {
                        SystemLog.Log(3, SystemLog.LogColor.Red, $"Fehler bei Befehl '{cmd}': {ex}");
                    }
                }
                else
                {
                    SystemLog.Log(2, SystemLog.LogColor.Yellow, $"Unbekannter Befehl: '{safeInput}'");
                }
            }
        }
    }
}
