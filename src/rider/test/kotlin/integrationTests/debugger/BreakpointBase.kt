package integrationTests.debugger

import com.intellij.execution.runners.ExecutionEnvironment
import com.intellij.openapi.options.SettingsEditor
import com.jetbrains.rd.ide.model.UnrealEngine
import com.jetbrains.rd.platform.diagnostics.LogTraceScenario
import com.jetbrains.rider.build.actions.BuildSolutionAction
import com.jetbrains.rider.diagnostics.LogTraceScenarios
import com.jetbrains.rider.run.configurations.RiderRunConfigurationBase
import com.jetbrains.rider.test.allure.Subsystem
import com.jetbrains.rider.test.annotations.TestEnvironment
import com.jetbrains.rider.test.env.enums.SdkVersion
import com.jetbrains.rider.test.env.enums.BuildTool
import com.jetbrains.rider.test.enums.PlatformType
import com.jetbrains.rider.test.framework.combine
import com.jetbrains.rider.test.scriptingApi.*
import io.qameta.allure.Epic
import io.qameta.allure.Feature
import org.testng.annotations.BeforeMethod
import org.testng.annotations.Test
import testFrameworkExtentions.EngineInfo
import testFrameworkExtentions.UnrealTestProject

@Epic(Subsystem.DEBUGGER)
@Feature("Breakpoints")
@TestEnvironment(
    platform = [PlatformType.WINDOWS_X64],
    buildTool = BuildTool.CPP,
    sdkVersion = SdkVersion.AUTODETECT
)
class BreakpointBase : UnrealTestProject() {
    init {
        projectDirectoryName = "EmptyUProject"
    }

    override val traceScenarios: Set<LogTraceScenario>
        get() = setOf(LogTraceScenarios.Debugger)

    override val traceCategories: List<String>
        get() = listOf("#com.jetbrains.cidr.execution.debugger")

    @BeforeMethod
    override fun prepareAndOpenSolution(parameters: Array<Any>){
        testDataDirectory.combine("additionalSource", "plugins", "DebugTestPlugin")
            .copyRecursively(activeSolutionDirectory.resolve("Plugins").resolve("DebugTestPlugin"))
        super.prepareAndOpenSolution(parameters)
    }

    @Test(dataProvider = "egsOnly_SlnOnly")
    fun toggleTest(@Suppress("UNUSED_PARAMETER") caseName: String, openWith: EngineInfo.UnrealOpenType, engine: UnrealEngine) {
        setConfigurationAndPlatform(project, "DebugGame Editor", "Win64")
        buildWithChecks(project, BuildSolutionAction(), "Build solution",
            useIncrementalBuild = false, timeout = buildTimeout)

        testDebugProgram(
            {
                toggleBreakpoint(project, "DebugTestPlugin.cpp", 12)
                toggleBreakpoint(project, "DebugTestPlugin.cpp", 17)
                toggleBreakpoint(project, "DebugTestPlugin.cpp", 28)
                toggleBreakpoint(project, "DebugTestPlugin.cpp", 30)
            }, {
                dumpProfile.customRegexToMask["<address>"] = Regex("0x[\\da-fA-F]{16}")

                waitForCidrPause()                  // 28: someNumber = Foo(someNumber);
                dumpFullCurrentData()
                toggleBreakpoint(project, "DebugTestPlugin.cpp", 30)
                toggleBreakpoint(project, "DebugTestPlugin.cpp", 30)
                toggleBreakpoint(project, "DebugTestPlugin.cpp", 32)
                resumeSession()
                waitForCidrPause()                  // 12: return fooNum * 2;
                dumpFullCurrentData()

                toggleBreakpoint(project, "DebugTestPlugin.cpp", 31)
                toggleBreakpoint(project, "DebugTestPlugin.cpp", 31)

                resumeSession()
                waitForCidrPause()                  // 17: return b * 3;
                dumpFullCurrentData()

                resumeSession()
                waitForCidrPause()                  // 30: someNumber = Moo(someNumber);
                dumpFullCurrentData()

                resumeSession()
                waitForCidrPause()                  // 12: return fooNum * 2;
                dumpFullCurrentData()

                resumeSession()
                waitForCidrPause()                  // 32: }
                dumpFullCurrentData()
            },
            exitProcessAfterTest = true
        )
    }

    private fun testDebugProgram(beforeRun: ExecutionEnvironment.() -> Unit, test: DebugTestExecutionContext.() -> Unit, exitProcessAfterTest: Boolean = false){
        withRunConfigurationEditorWithFirstConfiguration<RiderRunConfigurationBase, SettingsEditor<RiderRunConfigurationBase>>(project) {}
        testDebugProgram(project, testGoldFile, beforeRun, test, {}, exitProcessAfterTest)
    }
}