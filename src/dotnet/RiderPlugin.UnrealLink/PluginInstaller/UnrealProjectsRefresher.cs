using System;
using JetBrains.Annotations;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.ProjectsHost.SolutionHost.Progress;
using JetBrains.ReSharper.Feature.Services.Cpp.ProjectModel.UE4;
using JetBrains.ReSharper.Psi.Cpp.UE4;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.Util;
using JetBrains.Util.Interop;
using JetBrains.Util.Logging;
using RiderPlugin.UnrealLink.Model.FrontendBackend;
using RiderPlugin.UnrealLink.Resources;
using RiderPlugin.UnrealLink.Utils;

namespace RiderPlugin.UnrealLink.PluginInstaller
{
    public static class UnrealProjectsRefresher
    {
        private static readonly ILogger OurLogger = Logger.GetLogger(typeof(UnrealProjectsRefresher));

        public static void RefreshProjects(Lifetime parentLifetime, CppUE4Version myUnrealVersion,
            [NotNull] ISolution solution,
            [NotNull] UnrealPluginInstallInfo installInfo)
        {
            RefreshProjects(parentLifetime, solution, installInfo.ProjectPlugins.FirstOrDefault(null), installInfo.EngineRoot);
        }
        
        public static void RefreshProjects(Lifetime parentLifetime,
            [NotNull] ISolution solution,
            [CanBeNull] UnrealPluginInstallInfo.InstallDescription installDescription,
            [CanBeNull] VirtualFileSystemPath engineRoot)
        {
            parentLifetime.UsingNestedAsync(async lt =>
            {
                var lifetimeDefinition = lt.CreateNested();
                var lifetime = lifetimeDefinition.Lifetime;
                
                var unrealHost = solution.GetComponent<UnrealHost>();
                
                lifetime.Bracket(
                    () => unrealHost.myModel.RefreshInProgress.Value = true,
                    () => unrealHost.myModel.RefreshInProgress.Value = false
                );

                var task = BackgroundProgressBuilder.Create()
                    .AsCancelable(() =>
                    {
                        unrealHost.myModel.RiderLinkInstallMessage(
                            new InstallMessage(Strings.RefreshingProjectsFilesHasBeenCancelled_Text, ContentType.Error));
                        lifetimeDefinition.Terminate();
                    })
                    .WithHeader(Strings.RefreshingProjectFiles_Text);
                solution.GetComponent<BackgroundProgressManager>().AddNewTask(lifetime, task);
                var uprojectFile = installDescription?.UprojectPath;
                if (uprojectFile.IsNullOrEmpty())
                {
                    var cppUe4SolutionDetector = solution.GetComponent<ICppUE4SolutionDetector>();
                    uprojectFile = cppUe4SolutionDetector.GetUProjectPath();
                }

                await lifetime.StartBackground(() => RegenerateProjectFiles(lifetime, solution, unrealHost, engineRoot, uprojectFile));
            });
        }
        
        private static void RegenerateProjectFiles(Lifetime lifetime, ISolution solution,
            UnrealHost unrealHost,
            VirtualFileSystemPath engineRoot, VirtualFileSystemPath uprojectFile)
        {
            void LogFailedRefreshProjectFiles()
            {
                unrealHost.myModel.RiderLinkInstallMessage(new InstallMessage(Strings.FailedToRefreshProjectFiles_Text,
                    ContentType.Normal));
                unrealHost.myModel.RiderLinkInstallMessage(
                    new InstallMessage(Strings.RiderLinkPluginWillNotBeVisibleInThe_Text, ContentType.Normal));
                unrealHost.myModel.RiderLinkInstallMessage(new InstallMessage(
                    Strings.NeedToRefreshProjectFilesManually_Text,
                    ContentType.Normal));
            }
            if (!engineRoot.IsValidAndExistDirectory())
            {
                OurLogger.Warn($"[UnrealLink]: Couldn't find Unreal Engine root");

                LogFailedRefreshProjectFiles();
                return;
            }

            if (PlatformUtil.RuntimePlatform != JetPlatform.Windows) return;

            var ueVersion = solution.GetComponent<ICppUE4SolutionDetector>().UnrealContext.Value.Version;
            var pathToUnrealBuildToolBin = CppUE4FolderFinder.GetAbsolutePathToUnrealBuildToolBin(engineRoot, ueVersion);

            // 1. If project is under engine root, use GenerateProjectFiles.{extension} first
            if (GenerateProjectFilesCmd(lifetime, solution, unrealHost, uprojectFile, engineRoot)) return;
            // 2. If it's a standalone project, use UnrealVersionSelector
            //    The same way "Generate project files" from context menu of .uproject works
            if (RegenerateProjectUsingBundledUVS(lifetime, unrealHost, uprojectFile, engineRoot)) return;
            if (RegenerateProjectUsingSystemUVS(lifetime, solution, unrealHost, uprojectFile)) return;
            // 3. If UVS is missing or have failed, fallback to UnrealBuildTool
            if (RegenerateProjectUsingUBT(lifetime, unrealHost, uprojectFile, pathToUnrealBuildToolBin, engineRoot)) return;

            OurLogger.Warn("[UnrealLink]: Couldn't refresh project files");
            LogFailedRefreshProjectFiles();
        }

        private static bool GenerateProjectFilesCmd(Lifetime lifetime, ISolution solution, UnrealHost unrealHost,
            VirtualFileSystemPath uprojectFile, VirtualFileSystemPath engineRoot)
        {
            // Invalid uproject file means we couldn't get uproject file from solution detector and the project might be
            // under Engine source
            var invalidUprojectFile = !uprojectFile.IsValidAndExistFile();
            var isProjectUnderEngine = invalidUprojectFile || uprojectFile.StartsWith(engineRoot);
            if (!isProjectUnderEngine)
            {
                OurLogger.Info($"[UnrealLink]: {solution.SolutionFilePath} is not in {engineRoot} ");
                return false;
            }

            var generateProjectFilesCmd = engineRoot / $"GenerateProjectFiles.{CmdUtils.GetPlatformCmdExtension()}";
            if (!generateProjectFilesCmd.ExistsFile)
            {
                OurLogger.Info($"[UnrealLink]: {generateProjectFilesCmd} is not available");
                return false;
            }

            var pipeStreams = CreatePipeStreams(unrealHost, "[GenerateProjectFiles]:");
            var startInfo = CmdUtils.GetProcessStartInfo(pipeStreams, generateProjectFilesCmd, generateProjectFilesCmd.Directory);
            
            OurLogger.Info($"[UnrealLink]: Regenerating project files: {startInfo.Arguments}");
            try
            {
                var result = CmdUtils.RunCommandWithLock(lifetime, startInfo, OurLogger) == 0;
                if (!result)
                {
                    OurLogger.Warn($"[UnrealLink]: Failed refresh project files, calling {generateProjectFilesCmd} went wrong");
                }

                return result;
            }
            catch (ErrorLevelException errorLevelException)
            {
                OurLogger.Error(errorLevelException,
                    $"[UnrealLink]: Failed refresh project files, calling {generateProjectFilesCmd} went wrong");
                return false;
            }
        }

        private static bool RegenerateProjectUsingSystemUVS(Lifetime lifetime, ISolution solution, UnrealHost unrealHost,
            VirtualFileSystemPath uprojectFile)
        {
            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (programFiles.IsNullOrEmpty()) return false;
            
            var programFilesPath = VirtualFileSystemPath.Parse(programFiles, solution.GetInteractionContext());
            if (!programFilesPath.ExistsDirectory) return false;
            
            var pathToUnrealVersionSelector =
                programFilesPath / "Epic Games" / "Launcher" / "Engine"/ "Binaries" / "Win64" / "UnrealVersionSelector.exe";
            return RegenerateProjectUsingUVS(lifetime, unrealHost, uprojectFile, pathToUnrealVersionSelector);
        }
        
        private static bool RegenerateProjectUsingBundledUVS(Lifetime lifetime, UnrealHost unrealHost, VirtualFileSystemPath uprojectFile,
            VirtualFileSystemPath engineRoot)
        {
            var pathToUnrealVersionSelector =
                engineRoot / "Engine" / "Binaries" / "Win64" / "UnrealVersionSelector.exe";
            return RegenerateProjectUsingUVS(lifetime, unrealHost, uprojectFile, pathToUnrealVersionSelector);
        }

        private static bool RegenerateProjectUsingUVS(Lifetime lifetime, UnrealHost unrealHost, VirtualFileSystemPath uprojectFile,
            VirtualFileSystemPath pathToUnrealVersionSelector)
        {
            if (!uprojectFile.IsValidAndExistFile()) return false;
            if (!pathToUnrealVersionSelector.ExistsFile)
            {
                OurLogger.Info($"[UnrealLink]: {pathToUnrealVersionSelector} is not available");
                return false;
            }
            
            var pipeStreams = CreatePipeStreams(unrealHost,"[UVS]:");
            var startInfo = CmdUtils.GetProcessStartInfo(pipeStreams, pathToUnrealVersionSelector,
                pathToUnrealVersionSelector.Directory,
                "-projectFiles", $"\"{uprojectFile}\"");

            try
            {
                var result = CmdUtils.RunCommandWithLock(lifetime, startInfo, OurLogger) == 0;
                if (!result)
                {
                    OurLogger.Warn(
                        $"[UnrealLink]: Failed refresh project files: calling {pathToUnrealVersionSelector} {startInfo.Arguments}");
                }

                return result;
            }
            catch (ErrorLevelException errorLevelException)
            {
                OurLogger.Error(errorLevelException,
                    $"[UnrealLink]: Failed refresh project files: calling {pathToUnrealVersionSelector} {startInfo.Arguments}");
                return false;
            }
        }

        private static bool RegenerateProjectUsingUBT(Lifetime lifetime, UnrealHost unrealHost, VirtualFileSystemPath uprojectFile,
            VirtualFileSystemPath pathToUnrealBuildToolBin, VirtualFileSystemPath engineRoot)
        {
            if (uprojectFile.IsNullOrEmpty()) return false;
            
            bool isInstalledBuild = IsInstalledBuild(engineRoot);

            var pipeStreams = CreatePipeStreams(unrealHost, "[UBT]:");
            var startInfo = CmdUtils.GetProcessStartInfo(pipeStreams, pathToUnrealBuildToolBin,
                pathToUnrealBuildToolBin.Directory, "-ProjectFiles",
                $"-project=\"{uprojectFile.FullPath}\"", "-game", "-progress", isInstalledBuild ? "-rocket" : "-engine");
            try
            {
                var result = CmdUtils.RunCommandWithLock(lifetime, startInfo, OurLogger) == 0;
                if (!result)
                {
                    OurLogger.Warn($"[UnrealLink]: Failed refresh project files: calling {startInfo.Arguments}");
                }

                return result;
            }
            catch (ErrorLevelException errorLevelException)
            {
                OurLogger.Error(errorLevelException,
                    $"[UnrealLink]: Failed refresh project files: calling {startInfo.Arguments}");
                return false;
            }
        }
        
        private static InvokeChildProcess.PipeStreams CreatePipeStreams(UnrealHost unrealHost, string prefix)
        {
            return InvokeChildProcess.PipeStreams.Custom((chunk, isErr, logger) =>
            {
                unrealHost.myModel.RiderLinkInstallMessage(new InstallMessage(chunk,
                    isErr ? ContentType.Error : ContentType.Normal));
                    
                logger.Info(prefix + chunk);
            });
        }

        private static bool IsInstalledBuild(VirtualFileSystemPath engineRoot)
        {
            var installedBuildTxt = engineRoot / "Engine" / "Build" / "InstalledBuild.txt";
            var isInstalledBuild = installedBuildTxt.ExistsFile;
            return isInstalledBuild;
        }
    }
}