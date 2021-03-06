﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AldursLab.Essentials.Configs;
using AldursLab.Essentials.Extensions.DotNet;
using AldursLab.WurmAssistant.PublishRobot.Actions;
using AldursLab.WurmAssistant.PublishRobot.Parts;

namespace AldursLab.WurmAssistant.PublishRobot
{
    class Program
    {
        static void Main(string[] args)
        {
            PrintArgs(args);

            var assemblyDir = typeof (Program).Assembly.GetAssemblyLocationFullPath();// Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(assemblyDir, "temp");
            ClearDir(tempDir);

            ValidateArgs(args);
            var command = args[0];
            var configPath = Path.Combine(assemblyDir, command);
            if (!File.Exists(configPath))
            {
                throw new ArgumentException("config file does not exist, path: " + configPath);
            }

            IOutput output = new ConsoleOutput();
            IConfig config = new FileSimpleConfig(configPath);
            output.Write(config.ToString());

            var action = new PublishPackage(config, tempDir, output);
            action.Execute();
        }

        static void ValidateArgs(string[] args)
        {
            if (args.Length < 1 || !Regex.IsMatch(args[0], @"^.+\.cfg$"))
            {
                throw new ArgumentException("First argument should specify *.cfg, actual: " + args[0]);
            }
        }

        static void PrintArgs(string[] args)
        {
            var formattedArgs = "args: " + string.Join("\r\n", args);
            Console.WriteLine(formattedArgs);
        }

        static void ClearDir(string dirPath)
        {
            if (Directory.Exists(dirPath))
            {
                Directory.Delete(dirPath, recursive:true);
            }
            Directory.CreateDirectory(dirPath);
        }
    }
}
