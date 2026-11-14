import java.security.MessageDigest
import java.time.ZoneOffset
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import org.jetbrains.changelog.Changelog

// Built, signed and published the way the .NET Terminal Symbol Linker is (github.com/mlidbom/RiderTerminalSymbolLinker):
// the same Gradle plugins, the version from gradle.properties or the release tag, the change notes from CHANGELOG.md.
// What this plugin adds is its .NET part: compileDotNet builds it, and prepareSandbox puts it beside the jar.

plugins {
    kotlin("jvm") version "2.4.0"
    id("org.jetbrains.intellij.platform") version "2.16.0"
    id("org.jetbrains.changelog") version "2.5.0"
}

group = "dev.magnus"
version = providers.gradleProperty("pluginVersion").get()

val repositoryRoot: File = rootDir.parentFile

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        // Local builds may target an installed Rider (no multi-GB download); CI downloads the pinned one
        // (see gradle.properties).
        val localIdePath = providers.gradleProperty("localIdePath").orNull
        if (!localIdePath.isNullOrBlank()) {
            local(localIdePath)
        } else {
            rider(providers.gradleProperty("platformVersion").get())
        }
    }
}

intellijPlatform {
    pluginConfiguration {
        ideaVersion {
            // 253 = Rider 2025.3, the ReSharper SDK the .NET part is built against. No upper bound: a cap
            // disables the plugin on every new Rider until a release raises it.
            sinceBuild = "253"
            untilBuild = provider { null }
        }
        // Per-version "What's new" on the Marketplace listing, rendered from this version's CHANGELOG.md
        // section (falls back to Unreleased if the version has no section yet).
        changeNotes = provider {
            with(changelog) {
                renderItem(
                    (getOrNull(project.version.toString()) ?: getUnreleased())
                        .withHeader(false)
                        .withEmptySections(false),
                    Changelog.OutputType.HTML,
                )
            }
        }
    }

    // signPlugin/publishPlugin read these from the environment; buildPlugin alone needs none. CI supplies the
    // signing variables on release; publishPlugin also needs PUBLISH_TOKEN.
    signing {
        certificateChain = providers.environmentVariable("CERTIFICATE_CHAIN")
        privateKey = providers.environmentVariable("PRIVATE_KEY")
        password = providers.environmentVariable("PRIVATE_KEY_PASSWORD")
    }
    publishing {
        token = providers.environmentVariable("PUBLISH_TOKEN")
    }

    buildSearchableOptions = false
}

// Single source of truth for change notes (Keep a Changelog format): feeds the Marketplace changeNotes above and
// the GitHub release body (release.yml runs getChangelog).
changelog {
    path = repositoryRoot.resolve("CHANGELOG.md").path
    repositoryUrl = "https://github.com/mlidbom/dotnet-analysis-and-refactoring-mcp-for-rider"
}

kotlin {
    jvmToolchain(21)
    compilerOptions {
        // No forwarding methods for the platform interfaces' default methods: one was generated for
        // StatusBarWidget.getPresentation(PlatformType), which is deprecated, and the Marketplace's verifier reported
        // it as two usages of deprecated API. The platform's interfaces have real default methods to fall back on.
        jvmDefault = org.jetbrains.kotlin.gradle.dsl.JvmDefaultMode.NO_COMPATIBILITY
    }
}

// ---- The .NET part ------------------------------------------------------------------------------------------------

val host = "DotNetAnalysisAndRefactoringMcp"
val tools = "DotNetAnalysisAndRefactoringMcp.Tools"
val hostOutput: File = repositoryRoot.resolve("src/$host/bin/Release/net472")
val toolsOutput: File = repositoryRoot.resolve("src/$tools/bin/Release/net472")
val toolsBuildFolder = layout.buildDirectory.dir("tools-build")

// The tools project references the host's, so building it builds both.
val compileDotNet by tasks.registering(Exec::class) {
    workingDir = repositoryRoot
    commandLine("dotnet", "build", "src/$tools/$tools.csproj", "-c", "Release", "-v", "quiet", "-nologo")
}

// The tools build the plugin is installed with, as the host's ToolsBuildFolder reads one: a folder named by the time
// it was built, with the tools assembly, its symbols, and host.sha256, the SHA-256 of the host it goes with.
val assembleToolsBuild by tasks.registering {
    dependsOn(compileDotNet)
    outputs.dir(toolsBuildFolder)
    outputs.upToDateWhen { false }
    doLast {
        val root = toolsBuildFolder.get().asFile
        root.deleteRecursively()
        val name = ZonedDateTime.now(ZoneOffset.UTC).format(DateTimeFormatter.ofPattern("yyyy-MM-dd_HH-mm-ss-SSS")) + "0000"
        val build = root.resolve(name).apply { mkdirs() }
        listOf("dll", "pdb").forEach { toolsOutput.resolve("$tools.$it").copyTo(build.resolve("$tools.$it")) }
        val hostSha256 = MessageDigest.getInstance("SHA-256").digest(hostOutput.resolve("$host.dll").readBytes())
        build.resolve("host.sha256").writeText(hostSha256.joinToString("") { "%02x".format(it) })
    }
}

tasks.prepareSandbox {
    dependsOn(compileDotNet, assembleToolsBuild)
    from(hostOutput) {
        include("$host.dll", "$host.pdb")
        into("${rootProject.name}/dotnet")
    }
    from(toolsBuildFolder) {
        into("${rootProject.name}/tools")
    }
}
