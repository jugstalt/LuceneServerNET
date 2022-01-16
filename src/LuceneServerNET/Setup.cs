using LuceneServerNET.Extensions;
using System;
using System.Collections.Generic;
using System.IO;

namespace LuceneServerNET
{
    public class Setup
    {
        public bool TrySetup(string[] args)
        {
            try
            {
                if (SystemInfo.IsWindows)
                {
                    WindowPreSetup();
                }
                else if (SystemInfo.IsLinux)
                {
                    if (!String.IsNullOrEmpty(GetEnvironmentVariable("LUCENESERVER__ROOTPATH")))
                    {
                        return true;
                    }

                    LinuxPreSetup();
                }

                var fiConfig = new FileInfo("_config/luceneserver.json");
                if (!fiConfig.Exists)
                {
                    var fi = new FileInfo("_config/proto/_luceneserver.json");
                    if (fi.Exists)
                    {
                        string configContent = String.Empty;

                        Console.WriteLine("#################################################################################################################");
                        Console.WriteLine("Setup:");
                        Console.WriteLine("First start: creating simple _config/luceneserver.json. You can modify this file for your production settings...");
                        Console.WriteLine("#################################################################################################################");

                        if (SystemInfo.IsWindows)
                        {
                            configContent = WindowsSetup(fi.FullName, args);
                        }
                        else if (SystemInfo.IsLinux)
                        {
                            configContent = LinuxSetup(fi.FullName, args);
                        }

                        if (!String.IsNullOrEmpty(configContent))
                        {
                            Console.WriteLine(configContent);

                            var configTargetes = new List<string>(new string[] { $"{ fiConfig.Directory.FullName }/luceneserver.json" });

                            foreach (var configTarget in configTargetes)
                            {
                                fiConfig = new FileInfo(configTarget);

                                if (!fiConfig.Directory.Exists)
                                {
                                    fiConfig.Directory.Create();
                                }

                                File.WriteAllText(fiConfig.FullName, configContent);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Warning: can't intialize configuration for first start:");
                Console.WriteLine(ex.Message);

                return false;
            }

            return true;
        }

        #region Windows

        private void WindowPreSetup()
        {

        }

        private string WindowsSetup(string configTemplateFile, string[] args)
        {
            var fi = new FileInfo(configTemplateFile);

            var configText = File.ReadAllText(fi.FullName);
            configText = configText.Replace("{root-path}",  DataPath(configTemplateFile).ToJsonString());

            return configText;
        }

        #endregion

        #region Linux

        private const string EnvKey_DataPath = "LUCENESERVER_DATA_PATH";

        private void LinuxPreSetup()
        {

        }

        private string LinuxSetup(string configTemplateFile, string[] args)
        {
            string rootPath = DataPath(configTemplateFile);
            var fi = new FileInfo(configTemplateFile);

            var configText = File.ReadAllText(fi.FullName);
            configText = configText.Replace("{data-root-path}", rootPath.ToJsonString());

            return configText;
        }

        #endregion

        #region Helper

        private string GetEnvironmentVariable(string name)
        {
            var environmentVariables = Environment.GetEnvironmentVariables();

            if (environmentVariables.Contains(name) && !String.IsNullOrWhiteSpace(environmentVariables[name]?.ToString()))
            {
                return environmentVariables[name]?.ToString();
            }

            return null;
        }

        private string DataPath(string configTemplateFile)
        {
            if (SystemInfo.IsWindows)
            {
                var fi = new FileInfo(configTemplateFile);
                return $"{ fi.Directory.Parent.Parent.Parent.Parent.FullName }\\data";
            }
            else if (SystemInfo.IsLinux)
            {
                return GetEnvironmentVariable(EnvKey_DataPath) ?? "/etc/luceneserver/data";
            }
            throw new Exception("Setup: Unsupported OS!");
        }

        #endregion
    }
}
