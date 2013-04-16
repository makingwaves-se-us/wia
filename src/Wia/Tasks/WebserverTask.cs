﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Web.Administration;
using Wia.Commands;
using Wia.Model;
using Wia.Utility;

namespace Wia.Tasks {
    class WebserverTask : ITask {
        public IEnumerable<Type> DependsUpon() { return null; }

        public void Execute(WebsiteContext context) {
            using (ServerManager manager = new ServerManager()) {
                var webProjectDirectory = context.GetWebProjectDirectory();
                var siteAlreadyExists = manager.Sites.Any(s => s.Applications.Any(app => app.VirtualDirectories.Any(dir => dir.PhysicalPath == webProjectDirectory)));
                
                if (siteAlreadyExists) {
                    Logger.Warn("Site already exists in IIS.");
                    return;
                }

                var name = context.ProjectName;
                var host = new Uri(context.ProjectUrl).Host;

                // Create appool with project name
                var appPool = manager.ApplicationPools.Add(name);
                appPool.ManagedRuntimeVersion = GetFrameworkVersion(context);
                //appPool.AutoStart = true;
                appPool.Enable32BitAppOnWin64 = context.Enable32Bit;
                appPool.ManagedPipelineMode = context.AppPoolMode == AppPoolMode.Integrated
                                               ? ManagedPipelineMode.Integrated
                                               : ManagedPipelineMode.Classic;
								appPool.ProcessModel.PingingEnabled = false;
                if (!string.IsNullOrEmpty(Config.Instance.AppPoolUsername)) {
                    Logger.Log("Setting AppPool identity...");

                    var password = Config.Instance.AppPoolPassword;

                    if (string.IsNullOrEmpty(password)) {
                        Logger.Warn("Please fill in password for AppPool identity user!");
                        Console.WriteLine("\tUsername: " + Config.Instance.AppPoolUsername);
                        Console.Write("\tPassword: ");
                        password = ConsoleEx.ReadPassword();
                    }
                    
                    appPool.ProcessModel.IdentityType = ProcessModelIdentityType.SpecificUser;
                    appPool.ProcessModel.UserName = Config.Instance.AppPoolUsername;
                    appPool.ProcessModel.Password = password;
                }

                Logger.Log("Created a new AppPool with .NET {0} named {1}", appPool.ManagedRuntimeVersion, name);

                // Create site with appool.
                Site site = manager.Sites.Add(name, webProjectDirectory, 80);
                site.ServerAutoStart = true;
                site.Applications[0].ApplicationPoolName = name;
                Logger.Log("Created a new site named " + name);

                site.Bindings.Clear();
                site.Bindings.Add("*:80:" + host, "http");
                Logger.Log("Added binding for " + host);

                try {
                    manager.CommitChanges();
                    Logger.Success("Site and AppPool has been created in IIS.");
                }
                catch (Exception ex) {
                    Logger.Error(ex.ToString());
                    context.ExitAtNextCheck = true;
                }
            }
        }


        private static string GetFrameworkVersion(WebsiteContext context) {
            var frameworkVersion = context.FrameworkVersion;
            if (frameworkVersion <= 3.5) {
                frameworkVersion = 2;
            } 
            else if (frameworkVersion <= 4.5) {
                frameworkVersion = 4;
            }
            return string.Format("v{0:0.0}", frameworkVersion);
        }
    }
}