using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

static class StartupRegistration
{
    internal const string LogonArgument = "--startup";
    const string RegistrationSource = "RinMaintenance.Logon";
    const int LogonTrigger = 9;
    const int ExecuteAction = 0;
    const int InteractiveToken = 3;
    const int HighestRunLevel = 1;
    const int CreateOrUpdate = 6;
    const int IgnoreNewInstance = 2;

    static dynamic Connect()
    {
        Type schedulerType = Type.GetTypeFromProgID("Schedule.Service");
        if (schedulerType == null)
            throw new InvalidOperationException("Windows 任务计划程序不可用。");
        dynamic scheduler = Activator.CreateInstance(schedulerType);
        scheduler.Connect(null, null, null, null);
        return scheduler;
    }

    static dynamic GetOwnedTask(dynamic folder, string taskName)
    {
        dynamic task;
        try
        {
            task = folder.GetTask(taskName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (COMException ex)
        {
            if (ex.ErrorCode == unchecked((int)0x80070002))
                return null;
            throw;
        }

        if ((string)task.Definition.RegistrationInfo.Source != RegistrationSource)
            throw new InvalidOperationException("发现同名的其他启动任务，请在 Windows 任务计划程序中检查。");
        return task;
    }

    static string TaskName(WindowsIdentity identity)
    {
        return "RinMaintenance-Logon-" + identity.User.Value;
    }

    internal static bool IsEnabled()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            dynamic scheduler = Connect();
            dynamic folder = scheduler.GetFolder(@"\");
            dynamic task = GetOwnedTask(folder, TaskName(identity));
            return task != null && (bool)task.Enabled;
        }
    }

    internal static void SetEnabled(bool enabled)
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            dynamic scheduler = Connect();
            dynamic folder = scheduler.GetFolder(@"\");
            string taskName = TaskName(identity);
            dynamic task = GetOwnedTask(folder, taskName);
            if (!enabled)
            {
                if (task != null)
                    task.Enabled = false;
                return;
            }

            dynamic definition = CreateDefinition(scheduler, identity.Name, Application.ExecutablePath);
            folder.RegisterTaskDefinition(taskName, definition, CreateOrUpdate,
                identity.Name, null, InteractiveToken, null);
        }
    }

    internal static dynamic CreateDefinition(dynamic scheduler, string userName, string executable)
    {
        dynamic definition = scheduler.NewTask(0);
        definition.RegistrationInfo.Source = RegistrationSource;
        definition.RegistrationInfo.Description = "登录 Windows 后打开 QQ 和麦麦，并开启小凛管家的自动维护。";
        definition.Principal.UserId = userName;
        definition.Principal.LogonType = InteractiveToken;
        definition.Principal.RunLevel = HighestRunLevel;
        definition.Settings.Enabled = true;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.MultipleInstances = IgnoreNewInstance;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;

        dynamic trigger = definition.Triggers.Create(LogonTrigger);
        trigger.UserId = userName;
        trigger.Enabled = true;
        dynamic action = definition.Actions.Create(ExecuteAction);
        action.Path = executable;
        action.Arguments = LogonArgument;
        action.WorkingDirectory = Path.GetDirectoryName(executable);
        return definition;
    }
}
