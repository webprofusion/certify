using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Shared
{
    public class TaskScheduler
    {
        private const string SCHEDULED_TASK_NAME = "Certify Maintenance Task";
        private const string SCHEDULED_TASK_EXE = "certify.exe";
        private const string SCHEDULED_TASK_ARGS = "renew";

        public bool IsWindowsScheduledTaskPresent()
        {
            var taskList = Microsoft.Win32.TaskScheduler.TaskService.Instance.RootFolder.GetTasks();
            if (taskList.Any(t => t.Name == SCHEDULED_TASK_NAME))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Creates the windows scheduled task to perform renewals, running as the given userid (who
        /// should be admin level so they can perform cert mgmt and IIS management functions)
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="pwd"></param>
        /// <returns></returns>
        public bool CreateWindowsScheduledTask(string userId, string pwd)
        {
            // https://taskscheduler.codeplex.com/documentation
            var taskService = Microsoft.Win32.TaskScheduler.TaskService.Instance;
            try
            {
                var cliPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SCHEDULED_TASK_EXE);

                //setup auto renewal task, executing as admin using the given username and password
                var task = taskService.NewTask();

                task.Principal.RunLevel = Microsoft.Win32.TaskScheduler.TaskRunLevel.Highest;
                task.Actions.Add(new Microsoft.Win32.TaskScheduler.ExecAction(cliPath, SCHEDULED_TASK_ARGS));
                task.Triggers.Add(new Microsoft.Win32.TaskScheduler.DailyTrigger { DaysInterval = 1 });

                //register/update task
                taskService.RootFolder.RegisterTaskDefinition(SCHEDULED_TASK_NAME, task, Microsoft.Win32.TaskScheduler.TaskCreation.CreateOrUpdate, userId, pwd, Microsoft.Win32.TaskScheduler.TaskLogonType.Password);

                return true;
            }
            catch (Exception exp)
            {
                System.Diagnostics.Debug.WriteLine(exp.ToString());
                //failed to create task
                return false;
            }
        }

        public void DeleteWindowsScheduledTask()
        {
            Microsoft.Win32.TaskScheduler.TaskService.Instance.RootFolder.DeleteTask(SCHEDULED_TASK_NAME, exceptionOnNotExists: false);
        }
    }
}