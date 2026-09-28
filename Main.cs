using ioBroker_NewGen.Runtime;
using ioBroker_NewGen.Startup;

public static class MainClass
{
    static async Task Main(string[] args)
    {
        await OnStart.ExecuteAsync();
        await UnexpectedUserExitHandler.RunCommandLoopAsync();
    }
}
